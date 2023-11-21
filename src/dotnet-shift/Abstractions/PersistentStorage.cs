record class PersistentStorage
{
    public required string Name { get; init; }
    public required ResourceQuantity Size { get; init; }
    public required string Path { get; init; }
    public required ResourceQuantity? Limit { get; init; }
    public required string? StorageClass { get; init; }
    public required string Access { get; init; }
    public bool MountReadOnly { get; } = false;
}