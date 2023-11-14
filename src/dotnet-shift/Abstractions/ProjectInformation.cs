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
    }

    public required string DotnetVersion { get; init; }
    public required string AssemblyName { get; init; }
    public Dictionary<string, string> ContainerEnvironmentVariables { get; init; }
    public ContainerResources ContainerLimits { get; init; } = new();
    public ContainerPort[] ContainerPorts { get; init; }
    public ContainerPort? ExposedPort { get; init; }
    public PersistentStorage[] VolumeClaims { get; init; }
}