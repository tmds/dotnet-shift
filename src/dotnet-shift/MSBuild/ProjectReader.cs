namespace MSBuild;

using System.Linq;

sealed class ProjectReader : IProjectReader
{
    public ProjectInformation ReadProjectInfo(string path)
    {
        var project = new Microsoft.Build.Evaluation.Project(path);

        string? tfm = GetProperty(project, "TargetFramework");
        string? dotnetVersion = null;
        if (tfm is not null && tfm.StartsWith("net"))
        {
            dotnetVersion = tfm.Substring(3);
        }

        string? assemblyName = GetProperty(project, "AssemblyName");

        var info = new ProjectInformation()
        {
            DotnetVersion = dotnetVersion,
            AssemblyName = assemblyName
        };

        project.ProjectCollection.UnloadProject(project);

        return info;

        static string? GetProperty(Microsoft.Build.Evaluation.Project project, string name)
        {
            return project.AllEvaluatedProperties.FirstOrDefault(prop => prop.Name == name)?.EvaluatedValue;
        }
    }
}