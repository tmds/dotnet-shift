sealed record GitRepoInfo
{
    public required string RemoteBranch { get; init; }
    public required string RemoteUrl { get; init; }
}