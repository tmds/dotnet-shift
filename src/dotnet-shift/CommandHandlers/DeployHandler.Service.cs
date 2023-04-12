namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task<Service> ApplyAppService(
        IOpenShiftClient client,
        string name,
        Service? previous,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        CancellationToken cancellationToken)
    {
        Service service = CreateAppService(
                name,
                labels,
                selectorLabels);

        if (previous is null)
        {
            return await client.CreateServiceAsync(service, cancellationToken);
        }
        else
        {
            return await client.ReplaceServiceAsync(previous, service, update: null, cancellationToken);
        }
    }

    private static Service CreateAppService(
        string name,
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
                Type = null, // defaults to 'ClusterIP'.
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