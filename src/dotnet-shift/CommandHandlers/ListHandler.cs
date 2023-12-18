namespace CommandHandlers;

using OpenShift;

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
        using IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);

        List<Component> items = await GetItemsAsync(client, cancellationToken);

        PrintItems(items);

        return CommandResult.Success;
    }

    private async Task<HashSet<string>> FindDotnetComponentsAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        // Find .NET components by looking for ImageStreams, Deployments and BuildConfigs that have 'app.openshift.io/runtime' set to 'dotnet'.
        HashSet<string> names = new();

        string selector = $"{ResourceLabels.Runtime}={ResourceLabelValues.DotnetRuntime},{ResourceLabels.Name}";

        var deploymentsList = await client.ListDeploymentsAsync(selector, cancellationToken);
        foreach (var item in deploymentsList.Items)
        {
            names.Add(item.Metadata.Labels[ResourceLabels.Name]);
        }

        var imageStreamsList = await client.ListImageStreamsAsync(selector, cancellationToken);
        foreach (var item in imageStreamsList.Items)
        {
            names.Add(item.Metadata.Labels[ResourceLabels.Name]);
        }

        var buildConfigsList = await client.ListBuildConfigsAsync(selector, cancellationToken);
        foreach (var item in buildConfigsList.Items)
        {
            names.Add(item.Metadata.Labels[ResourceLabels.Name]);
        }

        return names;
    }

    private async Task<List<Component>> GetItemsAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        HashSet<string> componentNames = await FindDotnetComponentsAsync(client, cancellationToken);

        Dictionary<string, Component> components = new(componentNames.Select(name => KeyValuePair.Create(name, new Component() { Name = name })));

        string selector = $"{ResourceLabels.Name} in ({string.Join(", ", componentNames)})";
        var deploymentsList = await client.ListDeploymentsAsync(selector, cancellationToken);
        foreach (var item in deploymentsList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].Deployments.Add(item);
        }
        var imageStreamsList = await client.ListImageStreamsAsync(selector, cancellationToken);
        foreach (var item in imageStreamsList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].ImageStreams.Add(item);
        }
        var buildConfigsList = await client.ListBuildConfigsAsync(selector, cancellationToken);
        foreach (var item in buildConfigsList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].BuildConfigs.Add(item);
        }
        var pvcList = await client.ListPersistentVolumeClaimsAsync(selector, cancellationToken);
        foreach (var item in pvcList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].Pvcs.Add(item);
        }
        var configMapList = await client.ListConfigMapsAsync(selector, cancellationToken);
        foreach (var item in configMapList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].ConfigMaps.Add(item);
        }
        var servicesList = await client.ListServicesAsync(selector, cancellationToken);
        foreach (var item in servicesList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].Services.Add(item);
        }
        var routesList = await client.ListRoutesAsync(selector, cancellationToken);
        foreach (var item in routesList.Items)
        {
            components[item.Metadata.Labels[ResourceLabels.Name]].Routes.Add(item);
        }

        foreach (var component in components.Values)
        {
            foreach (var deployment in component.Deployments)
            {
                IDictionary<string, string>? podLabels = deployment.Spec.Template.Metadata.Labels;
                if (podLabels is null)
                {
                    continue;
                }

                string podSelector = GetPodSelector(podLabels);
                PodList podList = await client.ListPodsAsync(podSelector, cancellationToken);
                foreach (var item in podList.Items)
                {
                    component.Pods.Add(item);
                }
            }            
        }

        return components.Values.ToList();
    }

    private void PrintItems(List<Component> items)
    {
        // Sort.
        foreach (var item in items)
        {
            item.Deployments.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.BuildConfigs.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.Services.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.Routes.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.Pods.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
        }
        items.Sort((lhs, rhs) => (lhs.Name, lhs.Deployments.FirstOrDefault()?.GetName(), lhs.BuildConfigs.FirstOrDefault()?.GetName())
                                .CompareTo((rhs.Name, lhs.Deployments.FirstOrDefault()?.GetName(), rhs.BuildConfigs.FirstOrDefault()?.GetName())));

        // Format.
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(new[]{
            "COMPONENT",
            "DEPLOYMENT",
            "BUILD",
            "PVC",
            "CONFIG",
            "SERVICE",
            "ROUTE",
            "POD",
        });
        foreach (var item in items)
        {
            grid.AddRow(new[]{
                item.Name,
                string.Join("\n", item.Deployments.Select(FormatDeployment)),
                string.Join("\n", item.BuildConfigs.Select(FormatBuild)),
                string.Join("\n", item.Pvcs.Select(FormatPvc)),
                string.Join("\n", item.ConfigMaps.Select(FormatConfigMap)),
                string.Join("\n", item.Services.Select(FormatService)),
                string.Join("\n", item.Routes.Select(FormatRoute)),
                string.Join("\n", item.Pods.Select(FormatPod)),
            });
        }

        // Write.
        Console.Write(grid);

        static string FormatDeployment(Deployment deployment)
            => deployment.GetName();

        static string FormatBuild(BuildConfig build)
            => build.GetName();

        static string FormatService(Service service)
            => service.GetName();

        static string FormatPvc(PersistentVolumeClaim pvc)
            => pvc.GetName();

        static string FormatConfigMap(ConfigMap configMap)
            => configMap.GetName();

        static string FormatRoute(Route route)
            => !System.Console.IsOutputRedirected ? $"[link={route.GetRouteUrl()}]{route.Metadata.Name}[/]"
                                                  : route.Metadata.Name;

        static string FormatPod(Pod pod)
        {
            PodStatusPhase phase = pod.Status.Phase ?? PodStatusPhase.Unknown;
            return $"{pod.Metadata.Name} ({phase.ToString().Substring(0, 1)})";
        }
    }

    private string GetPodSelector(IDictionary<string, string> labels)
    {
        StringBuilder sb = new();

        foreach (var label in labels)
        {
            if (sb.Length != 0)
            {
                sb.Append(',');
            }
            sb.Append(label.Key);
            sb.Append('=');
            sb.Append(label.Value);
        }

        return sb.ToString();
    }

    sealed class Component
    {
        public required string Name { get; init; }

        public List<BuildConfig> BuildConfigs { get; } = new();
        public List<Deployment> Deployments { get; } = new();
        public List<ImageStream> ImageStreams { get; } = new();
        public List<PersistentVolumeClaim> Pvcs { get; } = new();
        public List<ConfigMap> ConfigMaps { get; } = new();
        public List<Service> Services { get; } = new();
        public List<Route> Routes { get; } = new();

        public List<Pod> Pods { get; } = new();
    }
}