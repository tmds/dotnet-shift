namespace MSBuild;

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Evaluation;

sealed class ProjectReader : IProjectReader
{
    private const string ASPNETCORE_URLS = nameof(ASPNETCORE_URLS);
    private const string ASPNETCORE_HTTP_PORTS = nameof(ASPNETCORE_HTTP_PORTS);
    private const string ASPNETCORE_HTTPS_PORTS = nameof(ASPNETCORE_HTTPS_PORTS);

    private static readonly string[] AspNetCoreEnvvars = new[] { ASPNETCORE_URLS, ASPNETCORE_HTTP_PORTS, ASPNETCORE_HTTPS_PORTS };

    public bool TryReadProjectInfo(string path, [NotNullWhen(true)] out ProjectInformation? projectInformation, out List<string> validationErrors)
    {
        validationErrors = new();
        var project = new Microsoft.Build.Evaluation.Project(path);

        string? dotnetVersion = ReadDotnetVersion(validationErrors, project);

        string? assemblyName = ReadAssemblyName(validationErrors, project);
        bool isAspNet = IsAspNet(project);

        Dictionary<string, string> environmentVariables = ReadEnvironmentVariables(project, isAspNet);

        ContainerResources containerLimits = ReadContainerLimits(validationErrors, project);
        List<ContainerPort> containerPorts = ReadContainerPorts(project, environmentVariables, validationErrors);

        if (validationErrors.Count > 0)
        {
            projectInformation = null;
            return false;
        }

        // We currently don't have a way to select the exposed port.
        IEnumerable<ContainerPort> servicePorts = containerPorts.Where(p => p.IsServicePort);
        ContainerPort? exposedPort = servicePorts.Count() == 1 ? servicePorts.First() : null;

        projectInformation = new ProjectInformation()
        {
            DotnetVersion = dotnetVersion!,
            AssemblyName = assemblyName!,
            ContainerEnvironmentVariables = environmentVariables,
            ContainerLimits = containerLimits,
            ContainerPorts = containerPorts.ToArray(),
            ExposedPort = exposedPort
        };

        project.ProjectCollection.UnloadProject(project);

        return true;
    }

    private static bool IsAspNet(Project project)
    {
        return GetItems(project, "FrameworkReference").Any(i => i.EvaluatedInclude == "Microsoft.AspNetCore.App");
    }

    private static string? GetProperty(Microsoft.Build.Evaluation.Project project, string name)
    {
        return project.AllEvaluatedProperties.FirstOrDefault(prop => prop.Name == name)?.EvaluatedValue;
    }

    private static ProjectItem[] GetItems(Project project, string name)
    {
        return project.AllEvaluatedItems.Where(item => item.ItemType == name).ToArray();
    }

    private static string? GetMetadata(ProjectItem item, string name)
    {
        return item.DirectMetadata.FirstOrDefault(m => m.Name == name)?.EvaluatedValue;
    }

    private static T? TryGetDictionaryValue<T>(Dictionary<string, T> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out T? value))
        {
            return value;
        }
        return default;
    }

    private static ContainerResources ReadContainerLimits(List<string> validationErrors, Project project)
    {
        Dictionary<string, ResourceQuantity> resourceLimits = new();
        foreach (var prop in new[] { "ContainerCpuRequest", "ContainerCpuLimit", "ContainerMemoryRequest", "ContainerMemoryLimit" })
        {
            string? value = GetProperty(project, prop);
            if (value != null)
            {
                if (ResourceQuantity.TryParse(value, out var quantity))
                {
                    resourceLimits.Add(prop, quantity);
                }
                else
                {
                    validationErrors.Add($"Cannot parse resource limit {prop} '{value}'.");
                }
            }
        }
        var containerLimits = new ContainerResources()
        {
            ContainerCpuRequest = TryGetDictionaryValue(resourceLimits, "ContainerCpuRequest"),
            ContainerCpuLimit = TryGetDictionaryValue(resourceLimits, "ContainerCpuLimit"),
            ContainerMemoryRequest = TryGetDictionaryValue(resourceLimits, "ContainerMemoryRequest"),
            ContainerMemoryLimit = TryGetDictionaryValue(resourceLimits, "ContainerMemoryLimit"),
        };
        return containerLimits;
    }

    private static Dictionary<string, string> ReadEnvironmentVariables(Project project, bool isAspNet)
    {
        Dictionary<string, string> environmentVariables = new();
        ProjectItem[] environmentVariableItems = GetItems(project, "ContainerEnvironmentVariable");
        foreach (var envvarItem in environmentVariableItems)
        {
            string? value = GetMetadata(envvarItem, "Value");
            if (value is not null)
            {
                environmentVariables[envvarItem.EvaluatedInclude] = value;
            }
        }
        // If the user hasn't set any of the envvars that bind ASP.NET, use http on port 8080.
        if (isAspNet && !AspNetCoreEnvvars.Any(envvar => environmentVariables.TryGetValue(envvar, out _)))
        {
            environmentVariables["ASPNETCORE_URLS"] = "http://*:8080";
        }
        return environmentVariables;
    }

    private static string? ReadDotnetVersion(List<string> validationErrors, Project project)
    {
        string? tfm = GetProperty(project, "TargetFramework");
        string? dotnetVersion = null;
        if (tfm is not null && tfm.StartsWith("net"))
        {
            dotnetVersion = tfm.Substring(3);
        }
        if (dotnetVersion is null)
        {
            validationErrors.Add($"Cannot determine project target framework version.");
        }

        return dotnetVersion;
    }

    private static string? ReadAssemblyName(List<string> validationErrors, Project project)
    {
        string? assemblyName = GetProperty(project, "AssemblyName");
        if (assemblyName is null)
        {
            validationErrors.Add($"Cannot determine application assembly name.");
        }

        return assemblyName;
    }

    private static Regex AspnetPortRegex = new(@"(?<scheme>\w+)://(?<domain>([*+]|).+):(?<port>\d+)");

    private static List<ContainerPort> ReadContainerPorts(Project project, Dictionary<string, string> environmentVariables, List<string> validationErrors)
    {
        List<ContainerPort> containerPorts = new();
        AddPortsFromEnvironment(containerPorts, environmentVariables, validationErrors);
        ProjectItem[] portItems = GetItems(project, "ContainerPort");
        foreach (var portItem in portItems)
        {
            AddContainerPort(containerPorts, name: GetMetadata(portItem, "Name"), type: GetMetadata(portItem, "Type"), port: portItem.EvaluatedInclude, isServicePort: GetMetadata(portItem, "IsServicePort"), validationErrors);
        }
        return containerPorts;
    }

    private static void AddContainerPort(List<ContainerPort> containerPorts, string? name, string? type, string port, string? isServicePort, List<string> validationErrors)
    {
        bool valid = true;
        if (!int.TryParse(port, out int portNumber))
        {
            validationErrors.Add($"Invalid port number '{port}'");
            valid = false;
        }
        type ??= "tcp";
        if (type is not ("tcp" or "udp"))
        {
            validationErrors.Add($"Invalid port type '{type}'");
            valid = false;
        }
        isServicePort ??= "false";
        if (isServicePort is not ("true" or "false"))
        {
            validationErrors.Add($"IsServicePort must be 'true' or 'false'.");
            valid = false;
        }
        if (isServicePort == "true" && string.IsNullOrEmpty(name))
        {
            validationErrors.Add($"Service ports must have a name.'");
            valid = false;
        }
        if (name is not null && containerPorts.Any(p => p.Name == name))
        {
            validationErrors.Add($"A port named '{name}' is added multiple times.");
            valid = false;
        }
        if (containerPorts.Any(p => p.Type == type && p.Port == portNumber))
        {
            validationErrors.Add($"Port '{type}/{portNumber}' is added multiple times.");
            valid = false;
        }
        if (valid)
        {
            containerPorts.Add(new ContainerPort()
            {
                Name = name,
                Port = portNumber,
                Type = type,
                IsServicePort = isServicePort == "true"
            });
        }
    }

    private static void AddPortsFromEnvironment(List<ContainerPort> ports, Dictionary<string, string> envvars, List<string> validationErrors)
    {
        if (envvars.TryGetValue(ASPNETCORE_URLS, out string? urls))
        {
            foreach (var url in Split(urls))
            {
                var match = AspnetPortRegex.Match(url);
                if (match.Success)
                {
                    AddContainerPort(ports, name: match.Groups["scheme"].Value, type: "tcp", port: match.Groups["port"].Value, isServicePort: "true", validationErrors);
                }
            }
            return;
        }

        if (envvars.TryGetValue(ASPNETCORE_HTTP_PORTS, out string? httpPorts))
        {
            foreach (var port in Split(httpPorts))
            {
                AddContainerPort(ports, "http", "tcp", port, isServicePort: "true", validationErrors);
            }
        }

        if (envvars.TryGetValue(ASPNETCORE_HTTP_PORTS, out string? httpsPorts))
        {
            foreach (var port in Split(httpsPorts))
            {
                AddContainerPort(ports, "https", "tcp", port, isServicePort: "true", validationErrors);
            }
        }

        static string[] Split(string input)
        {
            return input.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

}