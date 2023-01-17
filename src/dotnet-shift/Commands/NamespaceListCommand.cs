using System.CommandLine;

sealed class NamespaceListCommand : Command
{
    public NamespaceListCommand() : base("list", "Lists the accessible namespaces")
    {
        this.SetHandler(() => HandleAsync());
    }

    public static async Task<int> HandleAsync()
    {
        var client = new OpenShiftClient();

        List<Project> projects = await client.ListProjectsAsync();
        foreach (var project in projects)
        {
            Console.WriteLine(project.Name);
        }

        return 0;
    }
}
