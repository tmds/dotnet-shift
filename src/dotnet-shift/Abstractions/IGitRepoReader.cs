interface IGitRepoReader
{
    GitRepoInfo? ReadGitRepoInfo(string path);
}