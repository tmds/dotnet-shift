using OpenShift;

interface IOpenShiftClient
{
    string Namespace { get; }

    Task<User> GetUserAsync(CancellationToken cancellationToken);
    Task<ProjectList> ListProjectsAsync(CancellationToken cancellationToken);
    Task<DeploymentConfig?> GetDeploymentConfigAsync(string name, CancellationToken cancellationToken);
    Task<DeploymentConfigList> ListDeploymentConfigsAsync(string labelSelector, CancellationToken cancellationToken);
    Task DeleteDeploymentConfigAsync(string name, CancellationToken cancellationToken);
    Task CreateDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken);
    Task PatchDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken);
    Task<Deployment?> GetDeploymentAsync(string name, CancellationToken cancellationToken);
    Task<DeploymentList> ListDeploymentsAsync(string labelSelector, CancellationToken cancellationToken);
    Task DeleteDeploymentAsync(string name, CancellationToken cancellationToken);
    Task CreateBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken);
    Task PatchBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken);
    Task CreateConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken);
    Task CreateImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken);
    Task PatchImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken);
    Task CreateRouteAsync(Route route, CancellationToken cancellationToken);
    Task PatchRouteAsync(Route route, CancellationToken cancellationToken);
    Task CreateServiceAsync(Service service, CancellationToken cancellationToken);
    Task PatchServiceAsync(Service service, CancellationToken cancellationToken);
    Task<ConfigMap?> GetConfigMapAsync(string name, CancellationToken cancellationToken);
    Task<BuildConfig?> GetBuildConfigAsync(string name, CancellationToken cancellationToken);
    Task<ImageStream?> GetImageStreamAsync(string name, CancellationToken cancellationToken);
    Task<Route?> GetRouteAsync(string name, CancellationToken cancellationToken);
    Task<Service?> GetServiceAsync(string name, CancellationToken cancellationToken);
    Task<Build> StartBinaryBuildAsync(string buildConfigName, Stream archiveStream, CancellationToken cancellationToken);
    Task<Stream> FollowBuildLogAsync(string buildName, CancellationToken cancellationToken);
    Task<BuildConfigList> ListBuildConfigsAsync(string labelSelector, CancellationToken cancellationToken);
    Task<ConfigMapList> ListConfigMapsAsync(string labelSelector, CancellationToken cancellationToken);
    Task<ImageStreamList> ListImageStreamsAsync(string labelSelector, CancellationToken cancellationToken);
    Task<RouteList> ListRoutesAsync(string labelSelector, CancellationToken cancellationToken);
    Task<ServiceList> ListServicesAsync(string labelSelector, CancellationToken cancellationToken);
    Task DeleteImageStreamAsync(string name, CancellationToken cancellationToken);
    Task DeleteRouteAsync(string name, CancellationToken cancellationToken);
    Task DeleteConfigMapAsync(string name, CancellationToken cancellationToken);
    Task DeleteServiceAsync(string name, CancellationToken cancellationToken);
    Task DeleteBuildConfigAsync(string name, CancellationToken cancellationToken);
}