using Microsoft.Build.Tasks.Git;

namespace Git;

sealed class GitRepoReader : IGitRepoReader
{
    const string BranchPrefix = "refs/heads/";

    public GitRepoInfo? ReadGitRepoInfo(string path)
    {
        // The 'default' library for working with git in .NET is LibGit2Sharp.
        // However, this library brings over 15MB of compiled versions of libgit2 for various platforms
        // so we opt for a more light-weight implementation.

        string gitDirectory = Path.Combine(path, ".git");

        string headFilePath = Path.Combine(gitDirectory, "HEAD");
        string configFilePath = Path.Combine(gitDirectory, "config");

        if (!TryDetermineLocalBranch(headFilePath, out string? localBranchName))
        {
            return null;
        }

        if (!TryDetermineRemoteBranch(configFilePath, localBranchName, out string? remoteUrl, out string? remoteBranch))
        {
            return null;
        }

        return new GitRepoInfo()
        {
            RemoteBranch = remoteBranch,
            RemoteUrl = remoteUrl
        };
    }

    private bool TryDetermineRemoteBranch(string configFilePath,
                                          string localBranchName,
                                          [NotNullWhen(true)] out string? remoteUrl,
                                          [NotNullWhen(true)] out string? remoteBranch)
    {
        remoteUrl = null;
        remoteBranch = null;

        if (!File.Exists(configFilePath))
        {
            return false;
        }

        // We need a valid path to pass to the GitConfig.Reader which it won't use.
        string rootPath = Path.GetPathRoot(Path.GetTempPath())!;
        var reader = new GitConfig.Reader(rootPath, rootPath, new GitEnvironment());
        GitConfig config = reader.LoadFrom(configFilePath);

        string? remoteName = config.GetVariableValue("branch", localBranchName, "remote");
        if (remoteName is null)
        {
            return false;
        }
        string? remoteMerge = config.GetVariableValue("branch", localBranchName, "merge");
        if (remoteMerge is null ||
            !remoteMerge.StartsWith(BranchPrefix))
        {
            return false;
        }
        remoteBranch = remoteMerge.Substring(BranchPrefix.Length);

        remoteUrl = config.GetVariableValue("remote", remoteName, "url");

        return remoteUrl is not null && remoteBranch is not null;
    }

    private static bool TryDetermineLocalBranch(string headFilePath,
                                                [NotNullWhen(true)] out string? localBranchName)
    {
        localBranchName = null;

        if (!File.Exists(headFilePath))
        {
            return false;
        }

        string? line = File.ReadAllLines(headFilePath).FirstOrDefault();

        const string Prefix = $"ref: {BranchPrefix}";
        if (line?.StartsWith(Prefix) == true)
        {
            localBranchName = line.Substring(Prefix.Length);
        }

        return localBranchName is not null;
    }
}