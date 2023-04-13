sealed class MockProjectReader : IProjectReader
{
    private readonly ProjectInformation _projectInfo;

    public MockProjectReader(ProjectInformation info)
        => _projectInfo = info;

    public ProjectInformation ReadProjectInfo(string path)
        => _projectInfo;
}