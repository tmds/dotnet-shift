namespace CommandHandlers;

using System;
using OpenShift;

sealed partial class DeployHandler
{
    private async Task<Service> ApplyAppService(
        IOpenShiftClient client,
        string name,
        Service? previous,
        global::ContainerPort[] ports,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        CancellationToken cancellationToken)
    {
        Service service = CreateAppService(
                name,
                ports,
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
        global::ContainerPort[] ports,
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
                Ports = CreateServicePorts(ports),
            }
        };
    }

    private static List<ServicePort> CreateServicePorts(global::ContainerPort[] ports)
    {
        return ports.Where(p => p.IsServicePort).Select(p => new ServicePort()
        {
            Port = p.Port,
            Name = p.Name,
            Protocol = p.Type switch
            {
                "tcp" => ServicePortProtocol.TCP,
                "udp" => ServicePortProtocol.UDP,
                _ => throw new ArgumentException(p.Type)
            }
        }).ToList();
    }
}