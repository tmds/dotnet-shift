namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task ApplyAppRoute(
        IOpenShiftClient client,
        string name,
        Route? current,
        string serviceName,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        Route route = CreateAppRoute(
                name,
                current,
                serviceName,
                labels);

        if (current is null)
        {
            await client.CreateRouteAsync(route, cancellationToken);
        }
        else
        {
            await client.PatchRouteAsync(route, cancellationToken);
        }
    }

    private static Route CreateAppRoute(
        string name,
        Route? current,
        string serviceName,
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
                    TargetPort = "8080"
                }
            }
        };
    }
}