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
    private static string[] ValidAccesModes = new[] { "ReadWriteOnce", "ReadOnlyMany", "ReadWriteMany", "ReadWriteOncePod" };

    public bool TryReadProjectInfo(string path, [NotNullWhen(true)] out ProjectInformation? projectInformation, out List<string> validationErrors)
    {
        validationErrors = new();
        var project = new Microsoft.Build.Evaluation.Project(path);

        string? dotnetVersion = ReadDotnetVersion(validationErrors, project);

        string? assemblyName = ReadAssemblyName(validationErrors, project);
        bool isAspNet = IsAspNet(project);

        Dictionary<string, string> environmentVariables = ReadEnvironmentVariables(project, "ContainerEnvironmentVariable");
        // If the user hasn't set ASPNETCORE_URLS for an ASP.NET Core project, set a default value.
        if (isAspNet && !environmentVariables.TryGetValue(ASPNETCORE_URLS, out _))
        {
            environmentVariables["ASPNETCORE_URLS"] = "http://*:8080";
        }
        Dictionary<string, string> deploymentEnvironmentVariables = ReadEnvironmentVariables(project, "K8sEnvironmentVariable");
        ContainerResources containerLimits = ReadContainerLimits(validationErrors, project);
        ContainerPort[] containerPorts = ReadContainerPorts(project, environmentVariables, validationErrors);
        PersistentStorage[] volumeClaims = ReadVolumeClaims(project, validationErrors);
        ConfMap[] configMaps = ReadConfigMaps(project, validationErrors);
        bool enableImageStreamTagDeploymentTrigger = ReadEnableImageStreamTagDeploymentTrigger(project, validationErrors);
        HttpGetProbe? livenessProbe = ReadProbe(project, containerPorts, "Liveness", validationErrors);
        HttpGetProbe? readinessProbe = ReadProbe(project, containerPorts, "Readiness", validationErrors);
        HttpGetProbe? startupProbe = ReadProbe(project, containerPorts, "Startup", validationErrors);
        DeploymentStrategy? deploymentStrategy = ReadDeploymentStrategy(project, validationErrors);

        bool hasReadWriteOnceVolumes = volumeClaims.Any(c => c.Access == "ReadWriteOnce" || c.Access == "ReadWriteOncePod");
        if (hasReadWriteOnceVolumes)
        {
            if (deploymentStrategy == DeploymentStrategy.RollingUpdate)
            {
                validationErrors.Add($"K8sDeploymentStrategy 'RollingUpdate' can not be combined with 'ReadWriteOnce'/'ReadWriteOncePod' volumes.");
            }
        }

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
            DeploymentEnvironmentVariables = deploymentEnvironmentVariables,
            ContainerLimits = containerLimits,
            ContainerPorts = containerPorts,
            ExposedPort = exposedPort,
            VolumeClaims = volumeClaims,
            ConfigMaps = configMaps,
            EnableImageStreamTagDeploymentTrigger = enableImageStreamTagDeploymentTrigger,
            LivenessProbe = livenessProbe,
            ReadinessProbe = readinessProbe,
            StartupProbe = startupProbe,
            DeploymentStrategy = deploymentStrategy
        };

        project.ProjectCollection.UnloadProject(project);

        return true;
    }

    private DeploymentStrategy? ReadDeploymentStrategy(Project project, List<string> validationErrors)
    {
        string? strategy = GetProperty(project, $"K8sDeploymentStrategy");

        DeploymentStrategy? retval = strategy switch
        {
            "Recreate" => DeploymentStrategy.Recreate,
            "RollingUpdate" => DeploymentStrategy.RollingUpdate,
            _ => null
        };

        if (!string.IsNullOrEmpty(strategy) && !retval.HasValue)
        {
            validationErrors.Add($"K8sDeploymentStrategy '{strategy}' is not recognized. Allowed values are 'RollingUpdate'/'Recreate'.");
        }

        return retval;
    }

    private HttpGetProbe? ReadProbe(Project project, ContainerPort[] containerPorts, string probeName, List<string> validationErrors)
    {
        string? path = GetProperty(project, $"K8s{probeName}Path");
        string? port = GetProperty(project, $"K8s{probeName}Port");

        if (!TryGetPropertyAsInt(project, $"K8s{probeName}InitialDelay", out int? initialDelay))
        {
            validationErrors.Add($"K8s{probeName}InitialDelay is not an integer.");
        }
        if (!TryGetPropertyAsInt(project, $"K8s{probeName}Period", out int? period))
        {
            validationErrors.Add($"K8s{probeName}Period is not an integer.");
        }
        if (!TryGetPropertyAsInt(project, $"K8s{probeName}Timeout", out int? timeout))
        {
            validationErrors.Add($"K8s{probeName}Timeout is not an integer.");
        }
        if (!TryGetPropertyAsInt(project, $"K8s{probeName}FailureThresholdCount", out int? failureThresholdCount))
        {
            validationErrors.Add($"K8s{probeName}FailureThresholdCount is not an integer.");
        }
        if (path is null &&
            (port is not null || initialDelay.HasValue || period.HasValue || timeout.HasValue || failureThresholdCount.HasValue))
        {
            validationErrors.Add($"K8s{probeName}Path is required to enable the probe.");
        }
        if (path is not null)
        {
            port ??= "http";
            if (!containerPorts.Any(p => p.Name == port))
            {
                validationErrors.Add($"There is no port '{port}' defined for the '{probeName}' probe.");
            }
            else
            {
                return new HttpGetProbe()
                {
                    Path = path,
                    Port = port,
                    InitialDelay = initialDelay,
                    Period = period,
                    Timeout = timeout,
                    FailureThresholdCount = failureThresholdCount
                };
            }
        }
        return null;
    }

    private ConfMap[] ReadConfigMaps(Project project, List<string> validationErrors)
    {
        List<ConfMap> maps = new();
        ProjectItem[] items = GetItems(project, "K8sConfigMap");
        HashSet<string> names = new();
        foreach (var item in items)
        {
            string name = item.EvaluatedInclude;
            if (!names.Add(name))
            {
                validationErrors.Add("K8sConfigMap Name is not unique.");
            }
            string? path =  GetMetadata(item, "Path");
            if (path is null)
            {
                validationErrors.Add("K8sConfigMap must have a Path.");
            }
            else if (!path.StartsWith("/", StringComparison.InvariantCulture))
            {
                validationErrors.Add("K8sConfigMap Path is not an absolute path.");
            }
            if (!TryGetMetadataAsBool(item, "ReadOnly", out bool? mountReadOnly))
            {
                validationErrors.Add($"K8sConfigMap ReadOnly must be 'true' or 'false'.");
            }
            if (path is not null)
            {
                maps.Add(new ConfMap()
                {
                    Name = name,
                    Path = path,
                    MountReadOnly = mountReadOnly ?? true
                });
            }
        }
        return maps.ToArray();
    }

    private bool ReadEnableImageStreamTagDeploymentTrigger(Project project, List<string> validationErrors)
    {
        if (!TryGetPropertyAsBool(project, "K8sEnableImageStreamTagDeploymentTrigger", out bool? value))
        {
            validationErrors.Add($"K8sEnableImageStreamTagDeploymentTrigger must be 'true' or 'false'.");
        }

        return value ?? true;
    }

    private PersistentStorage[] ReadVolumeClaims(Project project, List<string> validationErrors)
    {
        List<PersistentStorage> claims = new();
        ProjectItem[] items = GetItems(project, "K8sPersistentStorage");
        HashSet<string> names = new();
        foreach (var item in items)
        {
            string name = item.EvaluatedInclude;
            if (!names.Add(name))
            {
                validationErrors.Add("K8sPersistentStorage Name is not unique.");
            }
            string? size =  GetMetadata(item, "Size");
            ResourceQuantity? sizeQuantity = null;
            if (size is null)
            {
                validationErrors.Add("K8sPersistentStorage must have a Size.");
            }
            else if (!ResourceQuantity.TryParse(size, out sizeQuantity))
            {
                validationErrors.Add($"K8sPersistentStorage Size '{size}' is not a valid quantity.");
            }
            string? path =  GetMetadata(item, "Path");
            if (path is null)
            {
                validationErrors.Add("K8sPersistentStorage must have a Path.");
            }
            else if (!path.StartsWith("/", StringComparison.InvariantCulture))
            {
                validationErrors.Add("K8sPersistentStorage Path is not an absolute path.");
            }
            ResourceQuantity? limitQuantity = null;
            string? limit =  GetMetadata(item, "Limit");
            if (limit is { Length: 0})
            {
                limit = null;
            }
            if (limit is not null && !ResourceQuantity.TryParse(limit, out limitQuantity))
            {
                validationErrors.Add($"K8sPersistentStorage Limit '{limit}' is not a valid quantity.");
            }
            string? storageClass =  GetMetadata(item, "StorageClass");
            if (storageClass is { Length: 0})
            {
                storageClass = null;
            }
            string? access =  GetMetadata(item, "Access");
            if (string.IsNullOrEmpty(access))
            {
                access = "ReadWriteOnce";
            }
            if (!IsValidAccessMode(access))
            {
                validationErrors.Add($"K8sPersistentStorage Access '{access}' is not a valid access mode.");
            }
            if (sizeQuantity is not null &&
                path is not null)
            {
                claims.Add(new PersistentStorage()
                {
                    Name = name,
                    Size = sizeQuantity,
                    Path = path,
                    Limit = limitQuantity,
                    StorageClass = storageClass,
                    Access = access
                });
            }
        }
        return claims.ToArray();
    }

    private bool IsValidAccessMode(string mode)
        => Array.IndexOf(ValidAccesModes, mode) != -1;

    private static bool IsAspNet(Project project)
    {
        return GetItems(project, "FrameworkReference").Any(i => i.EvaluatedInclude == "Microsoft.AspNetCore.App");
    }

    private static string? GetProperty(Microsoft.Build.Evaluation.Project project, string name)
    {
        return project.AllEvaluatedProperties.FirstOrDefault(prop => prop.Name == name)?.EvaluatedValue;
    }

    private static bool TryGetPropertyAsBool(Microsoft.Build.Evaluation.Project project, string name, out bool? value)
    {
        string? s = GetProperty(project, name);
        return TryParseBool(s, out value);
    }

    private static bool TryGetPropertyAsInt(Microsoft.Build.Evaluation.Project project, string name, out int? value)
    {
        string? s = GetProperty(project, name);
        if (s is null)
        {
            value = null;
            return true;
        }
        bool rv = int.TryParse(s, out int intValue);
        value = rv ? intValue : null;
        return rv;
    }

    private static ProjectItem[] GetItems(Project project, string name)
    {
        return project.AllEvaluatedItems.Where(item => item.ItemType == name).ToArray();
    }

    private static string? GetMetadata(ProjectItem item, string name)
    {
        return item.DirectMetadata.FirstOrDefault(m => m.Name == name)?.EvaluatedValue;
    }

    private static bool TryGetMetadataAsBool(ProjectItem item, string name, out bool? value)
    {
        string? s = GetMetadata(item, name);
        return TryParseBool(s, out value);
    }

    private static bool TryParseBool(string? s, out bool? value)
    {
        if (string.IsNullOrEmpty(s))
        {
            value = null;
            return true;
        }
        if (s == "true")
        {
            value = true;
            return true;
        }
        else if (s == "false")
        {
            value = false;
            return true;
        }
        else
        {
            value = null;
            return false;
        }
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
        foreach (var prop in new[] { "K8sCpuRequest", "K8sCpuLimit", "K8sMemoryRequest", "K8sMemoryLimit" })
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
            ContainerCpuRequest = TryGetDictionaryValue(resourceLimits, "K8sCpuRequest"),
            ContainerCpuLimit = TryGetDictionaryValue(resourceLimits, "K8sCpuLimit"),
            ContainerMemoryRequest = TryGetDictionaryValue(resourceLimits, "K8sMemoryRequest"),
            ContainerMemoryLimit = TryGetDictionaryValue(resourceLimits, "K8sMemoryLimit"),
        };
        return containerLimits;
    }

    private static Dictionary<string, string> ReadEnvironmentVariables(Project project, string itemName)
    {
        Dictionary<string, string> environmentVariables = new();
        ProjectItem[] environmentVariableItems = GetItems(project, itemName);
        foreach (var envvarItem in environmentVariableItems)
        {
            string? value = GetMetadata(envvarItem, "Value");
            if (value is not null)
            {
                environmentVariables[envvarItem.EvaluatedInclude] = value;
            }
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
            if (!TryGetMetadataAsBool(portItem, "IsServicePort", out bool? isServicePort))
            {
                validationErrors.Add($"ContainerPort IsServicePort must be 'true' or 'false'.");
            }
            if (!int.TryParse(portItem.EvaluatedInclude, out int portNumber))
            {
                validationErrors.Add($"ContainerPort Invalid port number '{portItem.EvaluatedInclude}'");
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