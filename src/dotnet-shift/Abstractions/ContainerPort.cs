public record ContainerPort
{
    public required int Port { get; init; }
    public required string Type { get; init; }
    public string? Name { get; init; }
    public bool IsServicePort { get; init; }
}