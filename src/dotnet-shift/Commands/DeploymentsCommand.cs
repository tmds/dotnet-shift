using System.CommandLine;

sealed class DeploymentsCommand : Command
{
    public DeploymentsCommand() : base("deployments")
    {
        this.SetHandler(() => HandleAsync());
    }

    public static async Task HandleAsync()
    {
        var client = new OpenShiftClient();

        var deployments = await client.ListDeploymentsAsync();

        foreach (var deployment in deployments)
        {
            Console.WriteLine(deployment.Name);
        }
    }
}