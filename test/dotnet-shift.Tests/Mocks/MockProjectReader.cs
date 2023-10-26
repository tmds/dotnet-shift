using System.Diagnostics.CodeAnalysis;

sealed class MockProjectReader : IProjectReader
{
    private readonly ProjectInformation _projectInfo;

    public MockProjectReader(ProjectInformation info)
        => _projectInfo = info;

    public bool TryReadProjectInfo(string path, [NotNullWhen(true)]out ProjectInformation? projectInformation, out List<string> validationErrors)
    {
        validationErrors = new();
        projectInformation = _projectInfo;
        return true;
    }
}