sealed record ProjectInformation
{
    public required string DotnetVersion { get; init; }
    public required string AssemblyName { get; init; }
    public required Dictionary<string, string> ContainerEnvironmentVariables { get; init; }
    public ContainerResources ContainerLimits { get; init; } = new();
}