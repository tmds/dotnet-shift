namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private static async Task<ConfigMap> ApplyAppConfigMap(
        IOpenShiftClient client,
        string name,
        ConfigMap? current,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        ConfigMap configMap = CreateAppConfigMap(
            name,
            current,
            labels);

        if (current is null)
        {
            return await client.CreateConfigMapAsync(configMap, cancellationToken);
        }
        else
        {
            return await client.PatchConfigMapAsync(configMap, cancellationToken);
        }
    }

    private static ConfigMap CreateAppConfigMap(
        string name,
        ConfigMap? current,
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
            },
            Data = current is not null ? null : // Don't touch existing data.
                new Dictionary<string, string>()
                {
                    { "appsettings.json", "{\n}" }
                }
        };
    }
}