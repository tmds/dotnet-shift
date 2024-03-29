namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task<Route> ApplyAppRoute(
        IOpenShiftClient client,
        string name,
        Route? previous,
        string serviceName,
        global::ContainerPort targetPort,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        Route route = CreateAppRoute(
                name,
                serviceName,
                targetPort,
                labels);

        if (previous is null)
        {
            return await client.CreateRouteAsync(route, cancellationToken);
        }
        else
        {
            return await client.ReplaceRouteAsync(previous, route, update: null, cancellationToken);
        }
    }

    private static Route CreateAppRoute(
        string name,
        string serviceName,
        global::ContainerPort targetPort,
        Dictionary<string, string> labels)
    {
        return new Route
        {
            ApiVersion = "route.openshift.io/v1",
            Kind = "Route",
            Metadata = new()
            {
                Name = name,
                Labels = labels
            },
            Spec = new()
            {
                To = new()
                {
                    Kind = "Service",
                    Name = serviceName
                },
                Port = new()
                {
                    TargetPort = targetPort.Port.ToString()
                }
            }
        };
    }
}