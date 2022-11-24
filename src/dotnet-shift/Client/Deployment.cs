record Deployment : IResource
{
    public required string Name { get; init; }
    public required Dictionary<string, string> Labels { get; init; }
}