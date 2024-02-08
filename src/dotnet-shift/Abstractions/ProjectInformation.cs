sealed record ProjectInformation
{
    public ProjectInformation()
    {
        // Set the default port in case no one initializes ContainerPorts to something else.
        ContainerPorts = new[]
        {
            new ContainerPort()
            {
                Port = 8080,
                Type = "tcp",
                Name = "http",
                IsServicePort = true
            }
        };

        ExposedPort = ContainerPorts[0];

        ContainerEnvironmentVariables = new();

        VolumeClaims = new PersistentStorage[] { };

        ConfigMaps = new ConfMap[] { };
    }

    public required string DotnetVersion { get; init; }
    public required string AssemblyName { get; init; }
    public Dictionary<string, string> ContainerEnvironmentVariables { get; init; }
    public ContainerResources ContainerLimits { get; init; } = new();
    public ContainerPort[] ContainerPorts { get; init; }
    public ContainerPort? ExposedPort { get; init; }
    public PersistentStorage[] VolumeClaims { get; init; }
    public ConfMap[] ConfigMaps { get; init; }
    public bool EnableImageStreamTagDeploymentTrigger { get; init; }

    public HttpGetProbe? LivenessProbe { get; init; }
    public HttpGetProbe? ReadinessProbe { get; init; }
    public HttpGetProbe? StartupProbe { get; init; }

    public DeploymentStrategy? DeploymentStrategy { get; init; }
}

sealed record HttpGetProbe
{
    public required string Path { get; init; }
    public required string Port { get; set; }
    public int? InitialDelay { get;  set; }
    public int? Period { get; set; }
    public int? Timeout { get; set; }
    public int? FailureThresholdCount { get; set; }
}

enum DeploymentStrategy
{
    Recreate,
    RollingUpdate
}