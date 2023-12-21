using OpenShift;

sealed class MockOpenShiftClient : IOpenShiftClient
{
    private readonly MockOpenShiftServer _server;

    public MockOpenShiftClient(MockOpenShiftServer server)
    {
        _server = server;
    }

    public Task<BuildConfig> CreateBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(buildConfig.Metadata.Name, buildConfig));

    public Task<ConfigMap> CreateConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(configMap.Metadata.Name, configMap));

    public Task<Deployment> CreateDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(deploymentConfig.Metadata.Name, deploymentConfig));

    public Task<ImageStream> CreateImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(imageStream.Metadata.Name, imageStream));

    public Task<Route> CreateRouteAsync(Route route, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(route.Metadata.Name, route));

    public Task<Service> CreateServiceAsync(Service service, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(service.Metadata.Name, service));

    public Task DeleteBuildConfigAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<BuildConfig>(name);
        return Task.CompletedTask;
    }

    public Task DeleteConfigMapAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<ConfigMap>(name);
        return Task.CompletedTask;
    }

    public Task DeleteDeploymentAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<Deployment>(name);
        return Task.CompletedTask;
    }

    public Task DeleteDeploymentConfigAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<DeploymentConfig>(name);
        return Task.CompletedTask;
    }

    public Task DeleteImageStreamAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<ImageStream>(name);
        return Task.CompletedTask;
    }

    public Task DeleteRouteAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<Route>(name);
        return Task.CompletedTask;
    }

    public Task DeleteServiceAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<Service>(name);
        return Task.CompletedTask;
    }

    public Task DeletePersistentVolumeClaimAsync(string name, CancellationToken cancellationToken)
    {
        _server.Delete<PersistentVolumeClaim>(name);
        return Task.CompletedTask;
    }

    public Task<Build?> GetBuildAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<Build>(name));

    public Task<BuildConfig?> GetBuildConfigAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<BuildConfig>(name));

    public Task<ConfigMap?> GetConfigMapAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<ConfigMap>(name));

    public Task<Deployment?> GetDeploymentAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<Deployment>(name));

    public Task<DeploymentConfig?> GetDeploymentConfigAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<DeploymentConfig>(name));

    public Task<ImageStream?> GetImageStreamAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<ImageStream>(name));

    public Task<Route?> GetRouteAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<Route>(name));

    public Task<Service?> GetServiceAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<Service>(name));

    public Task<PersistentVolumeClaim?> GetPersistentVolumeClaimAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(_server.Get<PersistentVolumeClaim>(name));

    public Task<PersistentVolumeClaim> CreatePersistentVolumeClaimAsync(PersistentVolumeClaim pvc, CancellationToken cancellationToken)
        => Task.FromResult(_server.Create(pvc.Metadata.Name, pvc));

    public Task<BuildConfigList> ListBuildConfigsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new BuildConfigList() { Items = _server.List<BuildConfig>(labelSelector) });

    public Task<ConfigMapList> ListConfigMapsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new ConfigMapList() { Items = _server.List<ConfigMap>(labelSelector) });

    public Task<DeploymentConfigList> ListDeploymentConfigsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new DeploymentConfigList() { Items = _server.List<DeploymentConfig>(labelSelector) });

    public Task<DeploymentList> ListDeploymentsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new DeploymentList() { Items = _server.List<Deployment>(labelSelector) });

    public Task<ImageStreamList> ListImageStreamsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new ImageStreamList() { Items = _server.List<ImageStream>(labelSelector) });

    public Task<PodList> ListPodsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new PodList() { Items = _server.List<Pod>(labelSelector) });

    public Task<ProjectList> ListProjectsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ProjectList() { Items = _server.List<Project>(null) });

    public Task<RouteList> ListRoutesAsync(string? labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new RouteList() { Items = _server.List<Route>(labelSelector) });

    public Task<ServiceList> ListServicesAsync(string? labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new ServiceList() { Items = _server.List<Service>(labelSelector) });

    public Task<PersistentVolumeClaimList> ListPersistentVolumeClaimsAsync(string? labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new PersistentVolumeClaimList() { Items = _server.List<PersistentVolumeClaim>(labelSelector) });

    public Task<BuildConfig> PatchBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken)
        => Task.FromResult(_server.Patch(buildConfig.Metadata.Name, buildConfig));

    public Task<ConfigMap> PatchConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken)
        => Task.FromResult(_server.Patch(configMap.Metadata.Name, configMap));

    public Task<Deployment> PatchDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken)
        => Task.FromResult(_server.Patch(deploymentConfig.Metadata.Name, deploymentConfig));

    public Task<ImageStream> PatchImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken)
        => Task.FromResult(_server.Patch(imageStream.Metadata.Name, imageStream));

    public Task<Route> PatchRouteAsync(Route route, CancellationToken cancellationToken)
        => Task.FromResult(_server.Patch(route.Metadata.Name, route));

    public Task<Service> PatchServiceAsync(Service service, CancellationToken cancellationToken)
        => Task.FromResult(_server.Patch(service.Metadata.Name, service));

    public Task<PersistentVolumeClaim> PatchPersistentVolumeClaimAsync(PersistentVolumeClaim value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<Deployment> ReplaceDeploymentAsync(Deployment value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<BuildConfig> ReplaceBuildConfigAsync(BuildConfig value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<ConfigMap> ReplaceConfigMapAsync(ConfigMap value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<DeploymentConfig> ReplaceDeploymentConfigAsync(DeploymentConfig value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<ImageStream> ReplaceImageStreamAsync(ImageStream value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<Route> ReplaceRouteAsync(Route value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public Task<Service> ReplaceServiceAsync(Service value, CancellationToken cancellationToken)
        => Task.FromResult(_server.Replace(value.Metadata.Name, value));

    public string Namespace => throw new NotImplementedException();

    public Task<User> GetUserAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> FollowBuildLogAsync(string buildName, CancellationToken cancellationToken)
     => Task.FromResult(_server.FollowBuildLog(buildName));

    public Task<Build> StartBinaryBuildAsync(string buildConfigName, Stream archiveStream, CancellationToken cancellationToken)
        => Task.FromResult(_server.StartBinaryBuild(buildConfigName, archiveStream));

    public Task<SecretList> ListSecretsAsync(string? labelSelector, string? fieldSelector, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public void Dispose()
    { }
}