namespace MSBuild;

using System;
using System.Linq;
using Microsoft.Build.Evaluation;

sealed class ProjectReader : IProjectReader
{
    public bool TryReadProjectInfo(string path, [NotNullWhen(true)] out ProjectInformation? projectInformation, out List<string> validationErrors)
    {
        validationErrors = new();
        var project = new Microsoft.Build.Evaluation.Project(path);

        string? tfm = GetProperty(project, "TargetFramework");
        string? dotnetVersion = null;
        if (tfm is not null && tfm.StartsWith("net"))
        {
            dotnetVersion = tfm.Substring(3);
        }

        string? assemblyName = GetProperty(project, "AssemblyName");

        if (dotnetVersion is null)
        {
            validationErrors.Add($"Cannot determine project target framework version.");
        }

        if (assemblyName is null)
        {
            validationErrors.Add($"Cannot determine application assembly name.");
        }

        ProjectItem[] environmentVariableItems = GetItems(project, "ContainerEnvironmentVariable");
        Dictionary<string, string> environmentVariables = new();
        foreach (var envvarItem in environmentVariableItems)
        {
            string? value = GetMetadata(envvarItem, "Value");
            if (value is not null)
            {
                environmentVariables[envvarItem.EvaluatedInclude] = value;
            }
        }

        Dictionary<string, ResourceQuantity> resourceLimits = new();
        foreach (var prop in new[] { "ContainerCpuRequest", "ContainerCpuLimit", "ContainerMemoryRequest", "ContainerMemoryLimit" })
        {
            string? value = GetProperty(project, prop);
            if (value != null)
            {
                if (ResourceQuantity.TryParse(value, out var quantity))
                {
                    resourceLimits.Add(prop, quantity);
                }
                else
                {
                    validationErrors.Add($"Cannot parse resource limit {prop} '{value}'.");
                }
            }
        }
        var containerLimits = new ContainerResources()
        {
            ContainerCpuRequest = TryGetDictionaryValue(resourceLimits, "ContainerCpuRequest"),
            ContainerCpuLimit = TryGetDictionaryValue(resourceLimits, "ContainerCpuLimit"),
            ContainerMemoryRequest = TryGetDictionaryValue(resourceLimits, "ContainerMemoryRequest"),
            ContainerMemoryLimit = TryGetDictionaryValue(resourceLimits, "ContainerMemoryLimit"),
        };

        if (validationErrors.Count > 0)
        {
            return false;
        }

        projectInformation = new ProjectInformation()
        {
            DotnetVersion = dotnetVersion!,
            AssemblyName = assemblyName!,
            ContainerEnvironmentVariables = environmentVariables,
            ContainerLimits = containerLimits,
        };

        project.ProjectCollection.UnloadProject(project);

        return true;

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

        static T? TryGetDictionaryValue<T>(Dictionary<string, T> dictionary, string key)
        {
            if (dictionary.TryGetValue(key, out T? value))
            {
                return value;
            }
            return default;
        }
    }
}