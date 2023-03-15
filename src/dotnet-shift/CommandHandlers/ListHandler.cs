namespace CommandHandlers;

sealed partial class ListHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private IOpenShiftClientFactory OpenShiftClientFactory { get; }

    public ListHandler(IAnsiConsole console, ILogger logger, IOpenShiftClientFactory clientFactory)
    {
        Console = console;
        Logger = logger;
        OpenShiftClientFactory = clientFactory;
    }

    public async Task<int> ExecuteAsync(LoginContext login, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();

        return CommandResult.Success;
    }

    private async Task<HashSet<string>> FindDotnetApplicationsAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        HashSet<string> apps = new();

        string selector = $"{ResourceLabels.Runtime}={ResourceLabelValues.DotnetRuntime},{ResourceLabels.PartOf}";

        var deploymentsList = await client.ListDeploymentConfigsAsync(selector, cancellationToken);
        foreach (var deployment in deploymentsList.Items)
        {
            apps.Add(deployment.Metadata.Labels[ResourceLabels.PartOf]);
        }

        var buildConfigsList = await client.ListBuildConfigsAsync(selector, cancellationToken);
        foreach (var buildConfig in buildConfigsList.Items)
        {
            apps.Add(buildConfig.Metadata.Labels[ResourceLabels.PartOf]);
        }

        return apps;
    }
}