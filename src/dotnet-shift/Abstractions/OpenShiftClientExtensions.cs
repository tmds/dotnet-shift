using OpenShift;
using System.Net;

static class OpenShiftClientExtensions
{
    public static async Task<Deployment> ReplaceDeploymentAsync(this IOpenShiftClient client, Deployment? previous, Deployment value, Action<Deployment, Deployment>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetDeploymentAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceDeploymentAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

    public static async Task<BuildConfig> ReplaceBuildConfigAsync(this IOpenShiftClient client, BuildConfig? previous, BuildConfig value, Action<BuildConfig, BuildConfig>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetBuildConfigAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceBuildConfigAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

    public static async Task<ConfigMap> ReplaceConfigMapAsync(this IOpenShiftClient client, ConfigMap? previous, ConfigMap value, Action<ConfigMap, ConfigMap>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetConfigMapAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceConfigMapAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

    public static async Task<DeploymentConfig> ReplaceDeploymentConfigAsync(this IOpenShiftClient client, DeploymentConfig? previous, DeploymentConfig value, Action<DeploymentConfig, DeploymentConfig>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetDeploymentConfigAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceDeploymentConfigAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

    public static async Task<ImageStream> ReplaceImageStreamAsync(this IOpenShiftClient client, ImageStream? previous, ImageStream value, Action<ImageStream, ImageStream>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetImageStreamAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceImageStreamAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

    public static async Task<Route> ReplaceRouteAsync(this IOpenShiftClient client, Route? previous, Route value, Action<Route, Route>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetRouteAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceRouteAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

    public static async Task<Service> ReplaceServiceAsync(this IOpenShiftClient client, Service? previous, Service value, Action<Service, Service>? update, CancellationToken cancellationToken)
    {
        do
        {
            previous ??= await client.GetServiceAsync(value.Metadata.Name, cancellationToken)
                         ?? throw new OpenShiftClientException("Resource not found.", HttpStatusCode.NotFound);

            update?.Invoke(previous, value);

            value.Metadata.ResourceVersion = previous.Metadata.ResourceVersion;

            try
            {
                return await client.ReplaceServiceAsync(value, cancellationToken);
            }
            catch (OpenShiftClientException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The object was changed, we need to refresh our object (and its resource version).
                previous = null;
            }
        } while (true);
    }

}