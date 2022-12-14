using System.CommandLine;

sealed class ListCommand : Command
{
    public ListCommand() : base("list", "Lists deployed .NET applications")
    {
        this.SetHandler(() => HandleAsync());
    }

    public static async Task HandleAsync()
    {
        var client = new OpenShiftClient();

        var deployments = await client.ListDotnetApplicationsAsync();

        Console.WriteLine("NAME");

        foreach (var deployment in deployments)
        {
            Console.WriteLine(deployment.Labels[ResourceLabels.PartOf]);
        }
    }
}