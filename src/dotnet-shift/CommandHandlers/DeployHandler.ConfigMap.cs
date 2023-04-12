namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private static async Task<ConfigMap> ApplyAppConfigMap(
        IOpenShiftClient client,
        string name,
        ConfigMap? previous,
        string runtime,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        ConfigMap configMap = CreateAppConfigMap(
            name,
            labels,
            runtime,
            initializeData: previous is null);

        if (previous is null)
        {
            return await client.CreateConfigMapAsync(configMap, cancellationToken);
        }
        else
        {
            // Patch to preserve user configuration.
            return await client.PatchConfigMapAsync(configMap, cancellationToken);
        }
    }

    private static ConfigMap CreateAppConfigMap(
        string name,
        Dictionary<string, string> labels,
        string? runtime,
        bool initializeData)
    {
        return new ConfigMap
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new()
            {
                Name = name,
                Labels = labels
            },
            Data = !initializeData || runtime is null ? null : CreateInitialConfigDataForRuntime(runtime)
        };
    }

    private static IDictionary<string, string>? CreateInitialConfigDataForRuntime(string runtime)
    {
        if (runtime == ResourceLabelValues.DotnetRuntime)
        {
            return new Dictionary<string, string>()
            {
                { "appsettings.json", "{\n}" }
            };
        }

        return null;
    }
}