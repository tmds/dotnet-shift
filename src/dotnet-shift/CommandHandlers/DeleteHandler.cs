namespace CommandHandlers;

using OpenShift;

sealed partial class DeleteHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private IOpenShiftClientFactory OpenShiftClientFactory { get; }

    public DeleteHandler(IAnsiConsole console, ILogger logger, IOpenShiftClientFactory clientFactory)
    {
        Console = console;
        Logger = logger;
        OpenShiftClientFactory = clientFactory;
    }

    public async Task<int> ExecuteAsync(LoginContext login, string app, CancellationToken cancellationToken)
    {
        IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);

        string selector = $"{ResourceLabels.PartOf}={app}";
        List<Resource> resources = await FindResourcesAsync(client, selector, cancellationToken);

        resources.Sort(DeleteOrder);

        await DeleteResourcesAsync(client, resources, cancellationToken);

        return CommandResult.Success;
    }

    private async Task DeleteResourcesAsync(IOpenShiftClient client, List<Resource> resources, CancellationToken cancellationToken)
    {
        foreach (var resource in resources)
        {
            Console.WriteLine($"Deleting {resource.Type} '{resource.Name}'");

            switch (resource.Type)
            {
                case ResourceType.ImageStream:
                    await client.DeleteImageStreamAsync(resource.Name, cancellationToken);
                    break;
                case ResourceType.ConfigMap:
                    await client.DeleteConfigMapAsync(resource.Name, cancellationToken);
                    break;
                case ResourceType.Route:
                    await client.DeleteRouteAsync(resource.Name, cancellationToken);
                    break;
                case ResourceType.Service:
                    await client.DeleteServiceAsync(resource.Name, cancellationToken);
                    break;
                case ResourceType.DeploymentConfig:
                    await client.DeleteDeploymentConfigAsync(resource.Name, cancellationToken);
                    break;
                case ResourceType.Deployment:
                    await client.DeleteDeploymentAsync(resource.Name, cancellationToken);
                    break;
                case ResourceType.BuildConfig:
                    await client.DeleteBuildConfigAsync(resource.Name, cancellationToken);
                    break;
                default:
                    throw new System.NotImplementedException($"{resource.Type} can not be deleted.");
            }
        }
    }

    private async Task<List<Resource>> FindResourcesAsync(IOpenShiftClient client, string selector, CancellationToken cancellationToken)
    {
        List<Resource> resources = new();

        await AppendResourcesAsync<ImageStream>(resources, selector, client,
            ResourceType.ImageStream,
            async (c, s, ct) => (await c.ListImageStreamsAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        await AppendResourcesAsync<ConfigMap>(resources, selector, client,
            ResourceType.ConfigMap,
            async (c, s, ct) => (await c.ListConfigMapsAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        await AppendResourcesAsync<Route>(resources, selector, client,
            ResourceType.Route,
            async (c, s, ct) => (await c.ListRoutesAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        await AppendResourcesAsync<Service>(resources, selector, client,
            ResourceType.Service,
            async (c, s, ct) => (await c.ListServicesAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        await AppendResourcesAsync<DeploymentConfig>(resources, selector, client,
            ResourceType.DeploymentConfig,
            async (c, s, ct) => (await c.ListDeploymentConfigsAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        await AppendResourcesAsync<BuildConfig>(resources, selector, client,
            ResourceType.BuildConfig,
            async (c, s, ct) => (await c.ListBuildConfigsAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        await AppendResourcesAsync<Deployment>(resources, selector, client,
            ResourceType.Deployment,
            async (c, s, ct) => (await c.ListDeploymentsAsync(s, ct)).Items,
            r => r.GetName(),
            r => r.Metadata.Labels,
            cancellationToken);

        return resources;
    }

    private static int DeleteOrder(Resource lhs, Resource rhs)
    {
        // The delete order makes .NET applications that are partially removed still show up on the 'list' command.
        // We remove resources without a runtime=dotnet label first, and remove DeploymentConfigs and BuildConfigs last.
        return (lhs.HasDotnetRuntimeLabel, TypeOrder(lhs.Type), lhs.Name)
                .CompareTo((rhs.HasDotnetRuntimeLabel, TypeOrder(rhs.Type), rhs.Name));

        static int TypeOrder(ResourceType type) => // The higher the number, the later it is removed.
            type switch
            {
                ResourceType.BuildConfig => 3,
                ResourceType.Deployment => 2,
                ResourceType.DeploymentConfig => 1,
                _ => 0
            };
    }

    enum ResourceType
    {
        ImageStream,
        ConfigMap,
        Route,
        Service,
        DeploymentConfig,
        Deployment,
        BuildConfig
    }

    private async Task AppendResourcesAsync<TResource>(
        List<Resource> resources,
        string selector,
        IOpenShiftClient client,
        ResourceType type,
        System.Func<IOpenShiftClient, string, CancellationToken, Task<IEnumerable<TResource>>> findResources,
        System.Func<TResource, string> getName,
        System.Func<TResource, IDictionary<string, string>> getLabels,
        CancellationToken cancellationToken)
    {
        var items = await findResources(client, selector, cancellationToken);
        foreach (var item in items)
        {
            string name = getName(item);
            bool hasDotnetRuntimeLabel = getLabels(item).TryGetValue(ResourceLabels.Runtime, out string? value) && value == ResourceLabelValues.DotnetRuntime;

            resources.Add(new Resource()
            {
                Name = name,
                Type = type,
                HasDotnetRuntimeLabel = hasDotnetRuntimeLabel
            });
        }
    }

    record Resource
    {
        public required ResourceType Type { get; init; }
        public required string Name { get; init; }
        public required bool HasDotnetRuntimeLabel { get; init; }
    }
}