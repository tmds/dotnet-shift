namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task ApplyAppService(
        IOpenShiftClient client,
        string name,
        Service? current,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        CancellationToken cancellationToken)
    {
        Service service = CreateAppService(
                name,
                current,
                labels,
                selectorLabels);

        if (current is null)
        {
            await client.CreateServiceAsync(service, cancellationToken);
        }
        else
        {
            await client.PatchServiceAsync(service, cancellationToken);
        }
    }

    private static Service CreateAppService(
        string name,
        Service? current,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels)
    {
        return new Service
        {
            ApiVersion = "v1",
            Kind = "Service",
            Metadata = new()
            {
                Name = name,
                Labels = labels
            },
            Spec = new()
            {
                Type = ServiceSpecType.ClusterIP,
                Selector = selectorLabels,
                Ports = new()
                {
                    new()
                    {
                        Protocol = ServicePortProtocol.TCP,
                        Port = 8080,
                        Name = "http"
                    }
                }
            }
        };
    }
}