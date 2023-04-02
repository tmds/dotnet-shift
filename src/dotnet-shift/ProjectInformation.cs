sealed record ProjectInformation
{
    public string? DotnetVersion { get; init; }
    public string? AssemblyName { get; init; }
    public required Dictionary<string, string> ContainerEnvironmentVariables { get; init; }
}