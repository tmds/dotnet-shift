using OpenShift;
using System.Reflection;
using System.Diagnostics;
using System.Net;

sealed class MockOpenShiftClient : IOpenShiftClient
{
    // Current state of the server.
    public object[] ServerResources => _resources.Values.ToArray();

    public T? GetResource<T>(string name)
    {
        _resources.TryGetValue((typeof(T), name), out object? value);
        return (T?)value;
    }

    private readonly Dictionary<(Type, string), object> _resources = new();

    // Tracks requests.
    public List<Request> Requests { get; } = new();

    public enum RequestType
    {
        Create,
        Get,
        List,
        Patch,
        Replace,
        Delete
    }

    public sealed class Request
    {
        public RequestType Type { get; init; }
        public object? Resource { get; init; }
        public string? Name { get; init; }
    }

    private void AddRequest(RequestType type, object? resource, string? name = null)
    {
        Requests.Add(new Request() { Type = type, Resource = resource, Name = name });
    }

    private T Create<T>(string name, T resource) where T : notnull
    {
        AddRequest(RequestType.Create, resource, name);

        var key = (typeof(T), name);
        if (_resources.ContainsKey(key))
        {
            throw new OpenShiftClientException("Resource exists", HttpStatusCode.Conflict);
        }
        _resources[key] = resource;

        return resource;
    }

    private void Delete<T>(string name) where T : class, new()
    {
        AddRequest(RequestType.Delete, new T(), name);

        var key = (typeof(T), name);
        bool removed = _resources.Remove(key);

        if (!removed)
        {
            throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);
        }
    }

    private T? Get<T>(string name) where T : class, new()
    {
        AddRequest(RequestType.Get, new T(), name);

        if (_resources.TryGetValue((typeof(T), name), out object? value))
        {
            return (T)value;
        }

        return null;
    }

    private List<T> List<T>(string? labelSelector, Func<T, IDictionary<string, string>> getLabels) where T : class, new()
    {
        AddRequest(RequestType.List, new T());

        List<T> list = new();

        foreach (var kvp in _resources)
        {
            if (kvp.Key.Item1 == typeof(T))
            {
                T value = (T)kvp.Value;
                if (MatchesSelector(getLabels(value), labelSelector))
                {
                    list.Add(value);
                }
            }
        }

        return list;
    }

    private T StrategicMergePatch<T>(string name, T value) where T : class
    {
        AddRequest(RequestType.Patch, value, name);

        T current = GetResource<T>(name) ?? throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);
        StrategicMergeObjectWith(current, value);
        return current;
    }

    private T Replace<T>(string name, T value, Func<T, string> getVersion) where T : class, new()
    {
        AddRequest(RequestType.Replace, value, name);

        T current = GetResource<T>(name) ?? throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);

        if (getVersion(current) != getVersion(value))
        {
            throw new OpenShiftClientException("Version mismatch", HttpStatusCode.Conflict);
        }

        var key = (typeof(T), name);
        _resources[key] = value;

        return value;
    }

    private void StrategicMergeObjectWith(object current, object mergeWith)
    {
        Debug.Assert(current.GetType() == mergeWith.GetType());

        const BindingFlags bindingFlags =
                BindingFlags.Instance |
                BindingFlags.Public;

        foreach (PropertyInfo propertyInfo in current.GetType().GetProperties(bindingFlags))
        {
            PatchMergeKeyAttribute? attribute = propertyInfo.GetCustomAttribute<PatchMergeKeyAttribute>();
            propertyInfo.SetValue(current, GetStrategicMergedValue(propertyInfo.GetValue(current), propertyInfo.GetValue(mergeWith), attribute?.MergeKey));
        }
    }

    private object? GetStrategicMergedValue(object? current, object? with, string? mergeKey)
    {
        if (with is null)
        {
            return current;
        }
        if (current is null)
        {
            return with;
        }

        Debug.Assert(current.GetType() == with.GetType());
        Type type = current.GetType();
        if (type == typeof(string))
        {
            return with;
        }
        // class from the OpenShift datamodel.
        if (type.FullName?.StartsWith("OpenShift.") == true)
        {
            StrategicMergeObjectWith(current, with);
            return current;
        }
        // value type
        if (type.IsValueType && type.FullName?.StartsWith("System.") == true)
        {
            return with;
        }
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            StrategicMergeDictionaryWith((System.Collections.IDictionary)current, (System.Collections.IDictionary)with);
            return current;
        }
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(List<>))
        {
            StrategicMergeListWith((System.Collections.IList)current, (System.Collections.IList)with, mergeKey);
            return current;
        }

        throw new NotImplementedException($"Unhandled type: {type.FullName}");
    }

    private void StrategicMergeDictionaryWith(System.Collections.IDictionary current, System.Collections.IDictionary mergeWith)
    {
        foreach (System.Collections.DictionaryEntry mergeValue in mergeWith)
        {
            if (current.Contains(mergeValue.Key))
            {
                current[mergeValue.Key] = GetStrategicMergedValue(current[mergeValue.Key], mergeValue.Value, null);
            }
            else
            {
                current[mergeValue.Key] = mergeValue.Value;
            }
        }
    }

    private void StrategicMergeListWith(System.Collections.IList current, System.Collections.IList mergeWith, string? mergeKey)
    {
        if (mergeKey is null)
        {
            foreach (var mergeValue in mergeWith)
            {
                current.Add(mergeValue);
            }
        }
        else
        {
            PropertyInfo? mergeKeyProperty = null;

            foreach (var mergeValue in mergeWith)
            {
                mergeKeyProperty ??= mergeValue.GetType().GetProperty(mergeKey);

                bool found = false;

                object mergeKeyValue = mergeKeyProperty!.GetValue(mergeValue)!;

                foreach (var item in current)
                {
                    if (mergeKeyProperty.GetValue(item) == mergeKeyValue)
                    {
                        StrategicMergeObjectWith(item, mergeValue);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    current.Add(mergeValue);
                }
            }
        }
    }

    private bool MatchesSelector(IDictionary<string, string> labels, string? labelSelector)
    {
        if (labelSelector is null)
        {
            return true;
        }
        throw new NotImplementedException();
    }

    public Task<BuildConfig> CreateBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken)
        => Task.FromResult(Create(buildConfig.Metadata.Name, buildConfig));

    public Task<ConfigMap> CreateConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken)
        => Task.FromResult(Create(configMap.Metadata.Name, configMap));

    public Task<Deployment> CreateDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken)
        => Task.FromResult(Create(deploymentConfig.Metadata.Name, deploymentConfig));

    public Task<ImageStream> CreateImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken)
        => Task.FromResult(Create(imageStream.Metadata.Name, imageStream));

    public Task<Route> CreateRouteAsync(Route route, CancellationToken cancellationToken)
        => Task.FromResult(Create(route.Metadata.Name, route));

    public Task<Service> CreateServiceAsync(Service service, CancellationToken cancellationToken)
        => Task.FromResult(Create(service.Metadata.Name, service));

    public Task DeleteBuildConfigAsync(string name, CancellationToken cancellationToken)
    {
        Delete<BuildConfig>(name);
        return Task.CompletedTask;
    }

    public Task DeleteConfigMapAsync(string name, CancellationToken cancellationToken)
    {
        Delete<ConfigMap>(name);
        return Task.CompletedTask;
    }

    public Task DeleteDeploymentAsync(string name, CancellationToken cancellationToken)
    {
        Delete<Deployment>(name);
        return Task.CompletedTask;
    }

    public Task DeleteDeploymentConfigAsync(string name, CancellationToken cancellationToken)
    {
        Delete<DeploymentConfig>(name);
        return Task.CompletedTask;
    }

    public Task DeleteImageStreamAsync(string name, CancellationToken cancellationToken)
    {
        Delete<ImageStream>(name);
        return Task.CompletedTask;
    }

    public Task DeleteRouteAsync(string name, CancellationToken cancellationToken)
    {
        Delete<Route>(name);
        return Task.CompletedTask;
    }

    public Task DeleteServiceAsync(string name, CancellationToken cancellationToken)
    {
        Delete<Service>(name);
        return Task.CompletedTask;
    }

    public Task<Build?> GetBuildAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<Build>(name));

    public Task<BuildConfig?> GetBuildConfigAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<BuildConfig>(name));

    public Task<ConfigMap?> GetConfigMapAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<ConfigMap>(name));

    public Task<Deployment?> GetDeploymentAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<Deployment>(name));

    public Task<DeploymentConfig?> GetDeploymentConfigAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<DeploymentConfig>(name));

    public Task<ImageStream?> GetImageStreamAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<ImageStream>(name));

    public Task<Route?> GetRouteAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<Route>(name));

    public Task<Service?> GetServiceAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult(Get<Service>(name));

    public Task<BuildConfigList> ListBuildConfigsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new BuildConfigList() { Items = List<BuildConfig>(labelSelector, c => c.Metadata.Labels) });

    public Task<ConfigMapList> ListConfigMapsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new ConfigMapList() { Items = List<ConfigMap>(labelSelector, c => c.Metadata.Labels) });

    public Task<DeploymentConfigList> ListDeploymentConfigsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new DeploymentConfigList() { Items = List<DeploymentConfig>(labelSelector, c => c.Metadata.Labels) });

    public Task<DeploymentList> ListDeploymentsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new DeploymentList() { Items = List<Deployment>(labelSelector, c => c.Metadata.Labels) });

    public Task<ImageStreamList> ListImageStreamsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new ImageStreamList() { Items = List<ImageStream>(labelSelector, c => c.Metadata.Labels) });

    public Task<PodList> ListPodsAsync(string labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new PodList() { Items = List<Pod>(labelSelector, c => c.Metadata.Labels) });

    public Task<ProjectList> ListProjectsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ProjectList() { Items = List<Project>(null, c => c.Metadata.Labels) });

    public Task<RouteList> ListRoutesAsync(string? labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new RouteList() { Items = List<Route>(labelSelector, c => c.Metadata.Labels) });

    public Task<ServiceList> ListServicesAsync(string? labelSelector, CancellationToken cancellationToken)
        => Task.FromResult(new ServiceList() { Items = List<Service>(labelSelector, c => c.Metadata.Labels) });

    public Task<BuildConfig> PatchBuildConfigAsync(BuildConfig buildConfig, CancellationToken cancellationToken)
        => Task.FromResult(StrategicMergePatch(buildConfig.Metadata.Name, buildConfig));

    public Task<ConfigMap> PatchConfigMapAsync(ConfigMap configMap, CancellationToken cancellationToken)
        => Task.FromResult(StrategicMergePatch(configMap.Metadata.Name, configMap));

    public Task<Deployment> PatchDeploymentAsync(Deployment deploymentConfig, CancellationToken cancellationToken)
        => Task.FromResult(StrategicMergePatch(deploymentConfig.Metadata.Name, deploymentConfig));

    public Task<ImageStream> PatchImageStreamAsync(ImageStream imageStream, CancellationToken cancellationToken)
        => Task.FromResult(StrategicMergePatch(imageStream.Metadata.Name, imageStream));

    public Task<Route> PatchRouteAsync(Route route, CancellationToken cancellationToken)
        => Task.FromResult(StrategicMergePatch(route.Metadata.Name, route));

    public Task<Service> PatchServiceAsync(Service service, CancellationToken cancellationToken)
        => Task.FromResult(StrategicMergePatch(service.Metadata.Name, service));

    public Task<Deployment> ReplaceDeploymentAsync(Deployment value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<BuildConfig> ReplaceBuildConfigAsync(BuildConfig value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<ConfigMap> ReplaceConfigMapAsync(ConfigMap value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<DeploymentConfig> ReplaceDeploymentConfigAsync(DeploymentConfig value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<ImageStream> ReplaceImageStreamAsync(ImageStream value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<Route> ReplaceRouteAsync(Route value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<Service> ReplaceServiceAsync(Service value, CancellationToken cancellationToken)
        => Task.FromResult(Replace(value.Metadata.Name, value, v => v.Metadata.ResourceVersion));

    public Task<Build> StartBinaryBuildAsync(string buildConfigName, Stream archiveStream, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public string Namespace => throw new NotImplementedException();

    public Task<User> GetUserAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> FollowBuildLogAsync(string buildName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}