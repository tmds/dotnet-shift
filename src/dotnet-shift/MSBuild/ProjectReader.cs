namespace MSBuild;

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Evaluation;

sealed class ProjectReader : IProjectReader
{
    // We don't use ASPNETCORE_HTTP_PORTS and ASPNETCORE_HTTPS_PORTS by design.
    // ASPNETCORE_URLS takes precedence over these and allows to configure all ports with a single setting.
    private const string ASPNETCORE_URLS = nameof(ASPNETCORE_URLS);

    public bool TryReadProjectInfo(string path, [NotNullWhen(true)] out ProjectInformation? projectInformation, out List<string> validationErrors)
    {
        validationErrors = new();
        var project = new Microsoft.Build.Evaluation.Project(path);

        string? dotnetVersion = ReadDotnetVersion(validationErrors, project);

        string? assemblyName = ReadAssemblyName(validationErrors, project);
        bool isAspNet = IsAspNet(project);

        Dictionary<string, string> environmentVariables = ReadEnvironmentVariables(project, isAspNet);

        ContainerResources containerLimits = ReadContainerLimits(validationErrors, project);
        ContainerPort[] containerPorts = ReadContainerPorts(project, environmentVariables, validationErrors);

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
            ContainerPorts = containerPorts,
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
        // If the user hasn't set ASPNETCORE_URLS for an ASP.NET Core project, set a default value.
        if (isAspNet && !environmentVariables.TryGetValue(ASPNETCORE_URLS, out _))
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

    private static ContainerPort[] ReadContainerPorts(Project project, Dictionary<string, string> environmentVariables, List<string> validationErrors)
    {
        ProjectItem[] portItems = GetItems(project, "ContainerPort");
        Dictionary<(string type, int port), (string? name, bool? isServicePort)> ports = new();
        HashSet<string> names = new();
        foreach (var portItem in portItems)
        {
            string? name = GetMetadata(portItem, "Name");
            string type = GetMetadata(portItem, "Type") ?? "tcp";
            if (type is not "tcp" and not "udp")
            {
                validationErrors.Add($"ContainerPort Type must be 'tcp' or 'udp'.");
            }
            bool? isServicePort = null;
            switch (GetMetadata(portItem, "IsServicePort"))
            {
                case null:
                    isServicePort = null;
                    break;
                case "true":
                    isServicePort = true;
                    break;
                case "false":
                    isServicePort = false;
                    break;
                default:
                    validationErrors.Add($"IsServicePort must be 'true' or 'false'.");
                    break;
            }
            if (!int.TryParse(portItem.EvaluatedInclude, out int portNumber))
            {
                validationErrors.Add($"Invalid port number '{portItem.EvaluatedInclude}'");
                portNumber = -1;
            }
            if (isServicePort == true && name is null)
            {
                validationErrors.Add($"ContainerPort Name must be set when IsServicePort is true.");
            }
            if (name is not null)
            {
                if (!names.Add(name))
                {
                    validationErrors.Add($"ContainerPort Name '{name}' is not unique.");
                }
            }
            if (ports.ContainsKey((type, portNumber)))
            {
                validationErrors.Add($"Port '{type}/{portNumber}' is added multiple times.");
            }
            if (portNumber != -1)
            {
                if (!ports.TryAdd((type, portNumber), (name, isServicePort)))
                {
                    validationErrors.Add($"Port '{type}/{portNumber}' is added multiple times.");
                }
            }
        }
        if (environmentVariables.TryGetValue(ASPNETCORE_URLS, out string? urls))
        {
            string type = "tcp";
            foreach (var url in Split(urls))
            {
                var match = AspnetPortRegex.Match(url);
                if (match.Success)
                {
                    string portMatch = match.Groups["port"].Value;
                    if (!int.TryParse(portMatch, out int portNumber))
                    {
                        validationErrors.Add($"Invalid port number '{portMatch}'");
                        continue;
                    }
                    string name = match.Groups["scheme"].Value;
                    if (ports.TryGetValue((type, portNumber), out (string? name, bool? isServicePort) current))
                    {
                        if (current.name is null)
                        {
                            if (!names.Add(name))
                            {
                                validationErrors.Add($"ASPNETCORE_URLS port name '{name}' is not unique. You can set the name using a ContainerPort.");
                            }
                        }
                        ports[(type, portNumber)] = (current.name ?? name, current.isServicePort ?? true);
                    }
                    else
                    {
                        if (!names.Add(name))
                        {
                            validationErrors.Add($"ASPNETCORE_URLS port name '{name}' is not unique. You can set the name using a ContainerPort.");
                        }
                        ports.Add((type, portNumber), (name, true));
                    }
                }
            }
        }
        return ports.Select(i => new ContainerPort()
        {
            IsServicePort = i.Value.isServicePort ?? false,
            Name = i.Value.name,
            Type = i.Key.type,
            Port = i.Key.port
        }).ToArray();

        static string[] Split(string input)
        {
            return input.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}