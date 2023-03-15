namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private static async Task CreateAppConfigMap(
        IOpenShiftClient client,
        string name,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        ConfigMap configMap = CreateAppConfigMap(
            name,
            labels);
        await client.CreateConfigMapAsync(configMap, cancellationToken);
    }

    private static ConfigMap CreateAppConfigMap(
        string name,
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
            Data = new Dictionary<string, string>()
            {
                { "appsettings.json", "{\n}" }
            }
        };
    }
}