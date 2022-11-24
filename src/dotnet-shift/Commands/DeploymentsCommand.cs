class DeploymentsCommand : System.CommandLine.Command
{
    public DeploymentsCommand() : base("deployments")
    {
        System.CommandLine.Handler.SetHandler(this, () => HandleAsync());
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