using OpenShift;

interface IOpenShiftClient
{
    string Namespace { get; }

    Task<User> GetUserAsync(CancellationToken cancellationToken);
    Task<ProjectList> ListProjectsAsync(CancellationToken cancellationToken);
    Task<DeploymentConfig?> GetDeploymentConfigAsync(string name, CancellationToken cancellationToken);
    Task<DeploymentConfigList> ListDeploymentConfigsAsync(string labelSelector, CancellationToken cancellationToken);
    Task DeleteDeploymentConfigAsync(string name, CancellationToken cancellationToken);
    Task<Deployment> CreateDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken);
    Task<Deployment> PatchDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken);
    Task<Deployment?> GetDeploymentAsync(string name, CancellationToken cancellationToken);
    Task<DeploymentList> ListDeploymentsAsync(string labelSelector, CancellationToken cancellationToken);
    Task DeleteDeploymentAsync(string name, CancellationToken cancellationToken);
    Task<BuildConfig> CreateBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken);
    Task<BuildConfig> PatchBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken);
    Task<ConfigMap> CreateConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken);
    Task<ConfigMap> PatchConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken);
    Task<ImageStream> CreateImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken);
    Task<ImageStream> PatchImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken);
    Task<Route> CreateRouteAsync(Route route, CancellationToken cancellationToken);
    Task<Route> PatchRouteAsync(Route route, CancellationToken cancellationToken);
    Task<Service> CreateServiceAsync(Service service, CancellationToken cancellationToken);
    Task<Service> PatchServiceAsync(Service service, CancellationToken cancellationToken);
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
    Task<RouteList> ListRoutesAsync(string? labelSelector, CancellationToken cancellationToken);
    Task<ServiceList> ListServicesAsync(string? labelSelector, CancellationToken cancellationToken);
    Task DeleteImageStreamAsync(string name, CancellationToken cancellationToken);
    Task DeleteRouteAsync(string name, CancellationToken cancellationToken);
    Task DeleteConfigMapAsync(string name, CancellationToken cancellationToken);
    Task DeleteServiceAsync(string name, CancellationToken cancellationToken);
    Task DeleteBuildConfigAsync(string name, CancellationToken cancellationToken);
    Task<PodList> ListPodsAsync(string labelSelector, CancellationToken cancellationToken);
    Task<Build?> GetBuildAsync(string name, CancellationToken cancellationToken);
    Task<Deployment> ReplaceDeploymentAsync(Deployment? previous, Deployment deployment, Action<Deployment, Deployment>? update, CancellationToken cancellationToken);
    Task<BuildConfig> ReplaceBuildConfigAsync(BuildConfig? previous, BuildConfig buildConfig, Action<BuildConfig, BuildConfig>? update, CancellationToken cancellationToken);
    Task<ConfigMap> ReplaceConfigMapAsync(ConfigMap? previous, ConfigMap configMap, Action<ConfigMap, ConfigMap>? update, CancellationToken cancellationToken);
    Task<DeploymentConfig> ReplaceDeploymentConfigAsync(DeploymentConfig? previous, DeploymentConfig deploymentConfig, Action<DeploymentConfig, DeploymentConfig>? update, CancellationToken cancellationToken);
    Task<ImageStream> ReplaceImageStreamAsync(ImageStream? previous, ImageStream imageStream, Action<ImageStream, ImageStream>? update, CancellationToken cancellationToken);
    Task<Route> ReplaceRouteAsync(Route? previous, Route route, Action<Route, Route>? update, CancellationToken cancellationToken);
    Task<Service> ReplaceServiceAsync(Service? previous, Service service, Action<Service, Service>? update, CancellationToken cancellationToken);
}