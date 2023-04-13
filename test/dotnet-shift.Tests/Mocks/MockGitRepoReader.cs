sealed class MockGitRepoReader : IGitRepoReader
{
    private readonly GitRepoInfo? _repoInfo;

    public MockGitRepoReader(GitRepoInfo? info)
        => _repoInfo = info;

    public GitRepoInfo? ReadGitRepoInfo(string path)
        => _repoInfo;
}