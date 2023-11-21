namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private static async Task<ConfigMap> ApplyConfigMap(
        IOpenShiftClient client,
        string name,
        ConfigMap? previous,
        ConfMap map,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        ConfigMap configMap = CreateAppConfigMap(
            name,
            map,
            labels);

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
        ConfMap map,
        Dictionary<string, string> labels)
    {
        return new ConfigMap
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new()
            {
                Name = name,
                Labels = labels
            }
        };
    }
}