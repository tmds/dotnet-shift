namespace MSBuild;

using System.Linq;
using Microsoft.Build.Evaluation;

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

        ProjectItem[] environmentVariableItems = GetItems(project, "ContainerEnvironmentVariable");
        Dictionary<string,string> environmentVariables = new();
        foreach (var envvarItem in environmentVariableItems)
        {
            string? value = GetMetadata(envvarItem, "Value");
            if (value is not null)
            {
                environmentVariables[envvarItem.EvaluatedInclude] = value;
            }
        }

        var info = new ProjectInformation()
        {
            DotnetVersion = dotnetVersion,
            AssemblyName = assemblyName,
            ContainerEnvironmentVariables = environmentVariables
        };

        project.ProjectCollection.UnloadProject(project);

        return info;

        static string? GetProperty(Microsoft.Build.Evaluation.Project project, string name)
        {
            return project.AllEvaluatedProperties.FirstOrDefault(prop => prop.Name == name)?.EvaluatedValue;
        }

        static ProjectItem[] GetItems(Project project, string name)
        {
            return project.AllEvaluatedItems.Where(item => item.ItemType == name).ToArray();
        }

        static string? GetMetadata(ProjectItem item, string name)
        {
            return item.DirectMetadata.FirstOrDefault(m => m.Name == name)?.EvaluatedValue;
        }
    }
}