using MSBuild;

interface IProjectReader
{
    ProjectInformation ReadProjectInfo(string path);
}