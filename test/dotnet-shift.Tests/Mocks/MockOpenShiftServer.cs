using OpenShift;
using System.Net;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

sealed partial class MockOpenShiftServer : IDisposable
{
    // Current state of the server.
    public object[] ResourceSpecs =>
        _resources.Values.Select(holder => holder.Value)
                         .Select(value =>
                                {
                                    Resource.SetResourceVersion(value, null);
                                    Resource.SetStatus(value, null);
                                    return value;
                                })
                        .OrderBy(value => value.GetType().FullName)
                        .ThenBy(value => Resource.GetName(value))
                        .ToArray();

    private readonly ConcurrentDictionary<(Type, string), IResourceController> _resources = new();

    public void Dispose()
    { }

    private bool TryAdd<T>(string name, ref T resource) where T : class
    {
        // Use a lock to ensure we only create a controller when we're sure we can add it.
        lock (_resources)
        {
            var key = (typeof(T), name);
            if (_resources.ContainsKey(key))
            {
                return false;
            }
            var controller = CreateController<T>(name, resource);
            _resources.TryAdd(key, controller);
            resource = controller.Value;
            return true;
        }
    }

    public void AddController<T>(Action<T> onSet) where T : class
    {
        _typeControllerFactories.Add(typeof(T), value => new ResourceController<T>((T)value, onSet));
    }

    public void AddController<T>(string name, Action<T> onSet) where T : class
    {
        _namedTypeControllerFactories.Add((typeof(T), name), value => new ResourceController<T>((T)value, onSet));
    }

    private Dictionary<Type, Func<object, IResourceController>> _typeControllerFactories = new();
    private Dictionary<(Type, string), Func<object, IResourceController>> _namedTypeControllerFactories = new();

    private IResourceController<T> CreateController<T>(string name, T resource) where T : class
    {
        Func<object, IResourceController>? factory;
        if (_namedTypeControllerFactories.TryGetValue((typeof(T), name), out factory))
        {
            return (IResourceController<T>)factory(resource);
        }
        if (_typeControllerFactories.TryGetValue(typeof(T), out factory))
        {
            return (IResourceController<T>)factory(resource);
        }
        return new ResourceController<T>(resource);
    }

    public T Create<T>(string name, T resource) where T : class
    {
        if (!TryAdd<T>(name, ref resource))
        {
            throw new OpenShiftClientException("Resource exists", HttpStatusCode.Conflict);
        }

        return resource;
    }

    public void Delete<T>(string name) where T : class, new()
    {
        var key = (typeof(T), name);
        if (!_resources.Remove(key, out IResourceController? holder))
        {
            throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);
        }
    }

    private bool TryGetResourceController<T>(string name, [NotNullWhen(true)]out IResourceController<T>? resourceController)
    {
        if (_resources.TryGetValue((typeof(T), name), out var controller))
        {
            resourceController = (IResourceController<T>)controller;
            return true;
        }
        resourceController = null;
        return false;
    }

    private bool TryGetResource<T>(string name, [NotNullWhen(true)]out T? resource) where T : class
    {
        if (TryGetResourceController<T>(name, out IResourceController<T>? resourceController))
        {
            resource = resourceController.Value;
            return true;
        }
        resource = null;
        return false;
    }

    private T GetResource<T>(string name) where T : class
    {
        TryGetResource<T>(name, out T? resource);
        return resource ?? throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);
    }

    public T? Get<T>(string name) where T : class, new()
    {
        TryGetResource<T>(name, out T? value);

        return value;
    }

    public List<T> List<T>(string? labelSelector) where T : class, new()
    {
        List<T> list = new();

        foreach (var kvp in _resources)
        {
            if (kvp.Key.Item1 == typeof(T))
            {
                T value = ((IResourceController<T>)kvp.Value).Value;
                IDictionary<string, string>? labels = Resource.GetLabels(value);
                if (MatchesSelector(labels, labelSelector))
                {
                    list.Add(value);
                }
            }
        }

        return list;
    }

    public T Patch<T>(string name, T value) where T : class
    {
        if (!TryGetResourceController<T>(name, out var resourceController))
        {
            throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);
        }

        return resourceController.Patch(value);
    }

    public T Replace<T>(string name, T value) where T : class, new()
    {
        if (!TryGetResourceController<T>(name, out var resourceController))
        {
            throw new OpenShiftClientException("Not found", HttpStatusCode.NotFound);
        }

        return resourceController.Replace(value);
    }

    public Build StartBinaryBuild(string buildConfigName, Stream archiveStream)
    {
        _ = new StreamReader(archiveStream).ReadToEnd();

        BuildConfig buildConfig = GetResource<BuildConfig>(buildConfigName);

        Build build = new()
        {
            Kind = "Build",
            ApiVersion = "build.openshift.io/v1",
            Metadata = new(),
            Status = new()
            {
                Phase = ""
            }
        };

        int i = 0;
        while (true)
        {
            string name = $"{buildConfigName}-{i}";
            build.Metadata.Name = name;
            if (TryAdd(name, ref build))
            {
                return build;
            }
        }
    }

    public Stream FollowBuildLog(string buildName)
    {
        // TODO
        return new MemoryStream(Encoding.UTF8.GetBytes("Application built successfully."));
    }

    private bool MatchesSelector(IDictionary<string, string>? labels, string? labelSelector)
    {
        if (labelSelector is null)
        {
            return true;
        }
        throw new NotImplementedException();
    }
}