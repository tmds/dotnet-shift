namespace CommandHandlers;

using Newtonsoft.Json;
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
        IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);

        List<Item> items = await GetItemsAsync(client, cancellationToken);

        PrintItems(items);

        return CommandResult.Success;
    }

    private async Task<List<Item>> GetItemsAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        string selector = $"{ResourceLabels.Runtime}={ResourceLabelValues.DotnetRuntime},{ResourceLabels.PartOf}";

        List<Item> items = await FindDotnetDeploymentsAsync(client, cancellationToken);
        Dictionary<(string type, string name), List<BuildConfig>> buildConfigs = await FindDotnetBuildConfigsAsync(client, cancellationToken);
        AddBuildConfigsToItems(items, buildConfigs);

        ServiceList services = await client.ListServicesAsync(null, cancellationToken);
        AddServicesToItems(items, services);

        RouteList routes = await client.ListRoutesAsync(null, cancellationToken);
        AddRoutesToItems(items, routes);

        await AddPodsToItemsAsync(client, items, cancellationToken);

        List<BuildConfig> unusedBuildConfigs = GetUnusedBuildConfigs(items, buildConfigs);
        foreach (var bc in unusedBuildConfigs)
        {
            items.Add(new Item()
            {
                App = bc.Metadata.Labels[ResourceLabels.PartOf],
                BuildConfigs = { bc }
            });
        }

        return items;
    }

    private static List<BuildConfig> GetUnusedBuildConfigs(List<Item> items, Dictionary<(string type, string name), List<BuildConfig>> buildConfigs)
    {
        List<BuildConfig> unusedBuildConfigs = buildConfigs.Values.SelectMany(v => v).ToList();

        // Remove all build configs that are used.
        foreach (var item in items)
        {
            foreach (var bc in item.BuildConfigs)
            {
                unusedBuildConfigs.Remove(bc);
            }
        }

        return unusedBuildConfigs;
    }

    private static void PrintItems(List<Item> items)
    {
        // Sort.
        foreach (var item in items)
        {
            item.BuildConfigs.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.Services.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.Routes.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
            item.Pods.Sort((lhs, rhs) => lhs.GetName().CompareTo(rhs.GetName()));
        }
        items.Sort((lhs, rhs) => (lhs.App, GetDeploymentName(lhs), lhs.BuildConfigs.FirstOrDefault()?.GetName())
                                .CompareTo((rhs.App, GetDeploymentName(rhs), rhs.BuildConfigs.FirstOrDefault()?.GetName())));

        // Format.
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(new[]{
            "APP",
            "DEPLOYMENT",
            "BUILD",
            "SERVICE",
            "ROUTE",
            "POD",
        });
        string? previousApp = null;
        foreach (var item in items)
        {
            grid.AddRow(new[]{
                item.App is null ? "(none)" : item.App == previousApp ? "" : item.App,
                FormatDeployment(item),
                string.Join("\n", item.BuildConfigs.Select(FormatBuild)),
                string.Join("\n", item.Services.Select(FormatService)),
                string.Join("\n", item.Routes.Select(FormatRoute)),
                string.Join("\n", item.Pods.Select(FormatPod)),
            });
            previousApp = item.App;
        }

        // Write.
        AnsiConsole.Write(grid);

        static string FormatDeployment(Item item)
            => item.Deployment is not null ? item.Deployment.Metadata.Name :
               item.DeploymentConfig is not null ? $"dc/{item.DeploymentConfig.Metadata.Name}" :
               "(none)";

        static string FormatBuild(BuildConfig build)
            => build.GetName();

        static string FormatService(Service service)
            => service.GetName();

        static string FormatRoute(Route route)
            => $"[link={route.GetRouteUrl()}]{route.Metadata.Name}[/]";

        static string FormatPod(Pod pod)
            => $"{pod.Metadata.Name} ({pod.Status.Phase})";
    }

    private async Task AddPodsToItemsAsync(IOpenShiftClient client, List<Item> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            IDictionary<string, string>? podLabels = GetPodLabels(item);
            if (podLabels is null)
            {
                continue;
            }

            string podSelector = GetPodSelector(podLabels);
            PodList pods = await client.ListPodsAsync(podSelector, cancellationToken);
            foreach (var pod in pods.Items)
            {
                item.Pods.Add(pod);
            }
        }
    }

    private static void AddRoutesToItems(List<Item> items, RouteList routes)
    {
        foreach (var route in routes.Items)
        {
            if (route.Spec.To.Kind != "Service")
            {
                continue;
            }
            string routeServiceName = route.Spec.To.Name;
            foreach (var item in items)
            {
                bool routeUsesService = item.Services.Any(s => s.Metadata.Name == routeServiceName);
                if (routeUsesService)
                {
                    item.Routes.Add(route);
                }
            }
        }
    }

    private static void AddServicesToItems(List<Item> items, ServiceList services)
    {
        foreach (var service in services.Items)
        {
            foreach (var item in items)
            {
                IDictionary<string, string>? podLabels = GetPodLabels(item);
                bool podTemplateMatchService = IsSubsetOf(service.Spec.Selector, podLabels);
                if (podTemplateMatchService)
                {
                    item.Services.Add(service);
                }
            }
        }
    }

    private void AddBuildConfigsToItems(List<Item> items, Dictionary<(string type, string name), List<BuildConfig>> buildConfigs)
    {
        foreach (var item in items)
        {
            HashSet<BuildConfig> itemBuildConfigs = new();

            if (item.Deployment is not null)
            {
                List<DeploymentTrigger>? triggers = null;
                if (item.Deployment.Metadata.Annotations.TryGetValue(Annotations.OpenShiftTriggers, out string? triggersAnnotation))
                {
                    triggers = JsonConvert.DeserializeObject<List<DeploymentTrigger>>(triggersAnnotation);
                }
                if (triggers is not null)
                {
                    foreach (var trigger in triggers)
                    {
                        if (trigger.FieldPath.StartsWith("spec.template.spec.containers") && trigger.FieldPath.EndsWith(".image"))
                        {
                            if (buildConfigs.TryGetValue((trigger.From.Kind, trigger.From.Name), out List<BuildConfig>? buildConfig))
                            {
                                AddRange(itemBuildConfigs, buildConfig);
                            }
                        }
                    }
                }
                foreach (var container in item.Deployment.Spec.Template.Spec.Containers)
                {
                    if (buildConfigs.TryGetValue(("DockerImage", container.Image), out List<BuildConfig>? buildConfig) ||
                        buildConfigs.TryGetValue(("ImageStreamTag", container.Image), out buildConfig))
                    {
                        AddRange(itemBuildConfigs, buildConfig);
                    }
                }
            }
            else if (item.DeploymentConfig is not null)
            {
                foreach (var container in item.DeploymentConfig.Spec.Template.Spec.Containers)
                {
                    if (buildConfigs.TryGetValue(("DockerImage", container.Image), out List<BuildConfig>? buildConfig) ||
                        buildConfigs.TryGetValue(("ImageStreamTag", container.Image), out buildConfig))
                    {
                        AddRange(itemBuildConfigs, buildConfig);
                    }
                    else
                    {
                        foreach (var trigger in item.DeploymentConfig.Spec.Triggers)
                        {
                            if (trigger.Type == "ImageChange" && trigger.ImageChangeParams.ContainerNames.Contains(container.Name))
                            {
                                if (buildConfigs.TryGetValue((trigger.ImageChangeParams.From.Kind, trigger.ImageChangeParams.From.Name), out buildConfig))
                                {
                                    AddRange(itemBuildConfigs, buildConfig);
                                }
                            }
                        }
                    }
                }
            }
            item.BuildConfigs.AddRange(itemBuildConfigs);
        }

        static void AddRange(HashSet<BuildConfig> itemBuildConfigs, List<BuildConfig> buildConfigs)
        {
            foreach (var bc in buildConfigs)
            {
                itemBuildConfigs.Add(bc);
            }
        }
    }

    private static async Task<Dictionary<(string kind, string name), List<BuildConfig>>> FindDotnetBuildConfigsAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        Dictionary<(string type, string name), List<BuildConfig>> buildConfigs = new();

        string selector = $"{ResourceLabels.Runtime}={ResourceLabelValues.DotnetRuntime},{ResourceLabels.PartOf}";

        var buildConfigsList = await client.ListBuildConfigsAsync(selector, cancellationToken);
        foreach (var buildConfig in buildConfigsList.Items)
        {
            var key = (buildConfig.Spec.Output.To.Kind, buildConfig.Spec.Output.To.Name);

            if (buildConfigs.TryGetValue(key, out var value))
            {
                value.Add(buildConfig);
            }
            else
            {
                buildConfigs.Add((buildConfig.Spec.Output.To.Kind, buildConfig.Spec.Output.To.Name), new() { buildConfig });
            }
        }

        return buildConfigs;
    }

    private static async Task<List<Item>> FindDotnetDeploymentsAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        List<Item> items = new();

        string selector = $"{ResourceLabels.Runtime}={ResourceLabelValues.DotnetRuntime},{ResourceLabels.PartOf}";

        var deploymentConfigsList = await client.ListDeploymentConfigsAsync(selector, cancellationToken);
        foreach (var deploymentConfig in deploymentConfigsList.Items)
        {
            items.Add(new Item()
            {
                App = deploymentConfig.Metadata.Labels[ResourceLabels.PartOf],
                DeploymentConfig = deploymentConfig
            });
        }

        var deploymentsList = await client.ListDeploymentsAsync(selector, cancellationToken);
        foreach (var deployment in deploymentsList.Items)
        {
            items.Add(new Item()
            {
                App = deployment.Metadata.Labels[ResourceLabels.PartOf],
                Deployment = deployment
            });
        }

        return items;
    }

    static string GetDeploymentName(Item item) =>
        item.Deployment?.GetName() ?? item.DeploymentConfig?.GetName() ?? "";

    private static IDictionary<string, string>? GetPodLabels(Item item)
        => item.Deployment?.Spec.Template.Metadata.Labels ??
           item.DeploymentConfig?.Spec.Template.Metadata.Labels;

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

    private static bool IsSubsetOf(IDictionary<string, string>? selector, IDictionary<string, string>? labels)
    {
        if (selector is null || labels is null)
        {
            return false;
        }

        foreach (var pair in selector)
        {
            if (!labels.TryGetValue(pair.Key, out string? value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    sealed class Item
    {
        public required string App { get; init; }
        public List<BuildConfig> BuildConfigs { get; } = new();
        public DeploymentConfig? DeploymentConfig { get; set; }
        public Deployment? Deployment { get; set; }
        public List<Service> Services { get; } = new();
        public List<Route> Routes { get; } = new();
        public List<Pod> Pods { get; } = new();
    }
}