interface IResource
{
    string Name { get; init; }
    Dictionary<string, string> Labels { get; }
}