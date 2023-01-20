using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task; // disambiguate with 'Task' class from the OpenShift client.

partial class OpenShiftClient
{
    private const string RuntimeLabel = "app.openshift.io/runtime";
    private const string Dotnet = "dotnet";

    private readonly OpenShift.OpenShiftApiClient _apiClient;

    public async Task<List<Deployment>> ListDotnetApplicationsAsync()
    {
        string selector = $"{ResourceLabels.Runtime}=dotnet,{ResourceLabels.PartOf}";
        var deploymentsList = await _apiClient.ListAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(Namespace, labelSelector: selector);
        var deployments = Map(deploymentsList.Items);
        return deployments;
    }

    public async Task DeleteApplicationAsync(string name)
    {
        string selector = $"{ResourceLabels.PartOf}={name}";
        Task[] tasks = new[]
        {
            DeleteServicesAsync(selector),
            DeleteImageStreamsAsync(selector),
            DeleteBuildConfigsAsync(selector),
            DeleteRoutesAsync(selector)
        };

        await Task.WhenAll(tasks);

        // Delete DeploymentConfigs last since we use them to detect the applications
        // in ListDotnetApplicationsAsync.
        await DeleteDeploymentConfigsAsync(selector);

        async Task DeleteDeploymentConfigsAsync(string selector)
        {
            var list = await _apiClient.ListAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(Namespace, labelSelector: selector);
            foreach (var item in list.Items)
            {
                await _apiClient.DeleteAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(item.Metadata.Name, Namespace);
            }
        }

        async Task DeleteServicesAsync(string selector)
        {
            var list = await _apiClient.ListCoreV1NamespacedServiceAsync(Namespace, labelSelector: selector);
            foreach (var item in list.Items)
            {
                await _apiClient.DeleteCoreV1NamespacedServiceAsync(item.Metadata.Name, Namespace);
            }
        }

        async Task DeleteImageStreamsAsync(string selector)
        {
            var list = await _apiClient.ListImageOpenshiftIoV1NamespacedImageStreamAsync(Namespace, labelSelector: selector);
            foreach (var item in list.Items)
            {
                await _apiClient.DeleteImageOpenshiftIoV1NamespacedImageStreamAsync(item.Metadata.Name, Namespace);
            }
        }

        async Task DeleteBuildConfigsAsync(string selector)
        {
            var list = await _apiClient.ListBuildOpenshiftIoV1NamespacedBuildConfigAsync(Namespace, labelSelector: selector);
            foreach (var item in list.Items)
            {
                await _apiClient.DeleteBuildOpenshiftIoV1NamespacedBuildConfigAsync(item.Metadata.Name, Namespace);
            }
        }

        async Task DeleteRoutesAsync(string selector)
        {
            var list = await _apiClient.ListRouteOpenshiftIoV1NamespacedRouteAsync(Namespace, labelSelector: selector);
            foreach (var item in list.Items)
            {
                await _apiClient.DeleteRouteOpenshiftIoV1NamespacedRouteAsync(item.Metadata.Name, Namespace);
            }
        }
    }

    public async Task<User> GetUserAsync()
    {
        var user = await _apiClient.ReadUserAsync("~");
        return Map(user);
    }

    public async Task<List<Project>> ListProjectsAsync()
    {
        var projectList = await _apiClient.ListProjectOpenshiftIoV1ProjectAsync();
        var projects = projectList.Items.Select(Map);
        projects = projects.OrderBy(projectList => projectList.Name, StringComparer.Ordinal);
        return projects.ToList();
    }

    public async Task CreateProjectAsync(string ns)
    {
        OpenShift.ProjectRequest request = new()
        {
            Metadata = new()
            {
                Name = ns
            }
        };
        await _apiClient.CreateProjectOpenshiftIoV1ProjectRequestAsync(request);
    }

    public async Task ApplyDeploymentConfigAsync(string deploymentConfig)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.DeploymentConfig>(deploymentConfig);
        try
        {
            await _apiClient.CreateAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(body, Namespace);
        }
        catch (Exception ex) when (IsResourceExists(ex))
        {
            string name = body.Metadata.Name;

            // match the current resource version.
            var current = await _apiClient.ReadAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(name, Namespace);
            body.Metadata.ResourceVersion = current.Metadata.ResourceVersion;

            await _apiClient.ReplaceAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(body, name, Namespace);
        }
    }

    public async Task<bool> CreateImageStreamTagAsync(string imageStreamName, string tagReference)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.TagReference>(tagReference);
        try
        {
            string tagName = body.Name;

            OpenShift.ImageStream imageStream = await _apiClient.ReadImageOpenshiftIoV1NamespacedImageStreamAsync(imageStreamName, Namespace);
            foreach (var tag in imageStream.Spec.Tags)
            {
                if (tag.Name == tagName)
                {
                    // tag exists.
                    return false;
                }
            }

            imageStream.Spec.Tags.Add(body);
            await _apiClient.ReplaceImageOpenshiftIoV1NamespacedImageStreamAsync(imageStream, imageStreamName, Namespace);
            return true;
        }
        catch (Exception ex) when (IsResourceNotFound(ex))
        {
            OpenShift.ImageStream imageStream = new()
            {
                Spec = new()
                {
                    Tags = new() { body }
                },
                Metadata = new()
                {
                    Name = imageStreamName
                }
            };
            await _apiClient.CreateImageOpenshiftIoV1NamespacedImageStreamAsync(imageStream, Namespace);
            return true;
        }
    }

    public async Task ApplyServiceAsync(string service)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.Service2>(service);
        try
        {
            await _apiClient.CreateCoreV1NamespacedServiceAsync(body, Namespace);
        }
        catch (Exception ex) when (IsResourceExists(ex))
        {
            string name = body.Metadata.Name;

            await _apiClient.ReplaceCoreV1NamespacedServiceAsync(body, name, Namespace);
        }
    }

    public async Task CreateSecretAsync(string secret)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.Secret>(secret);
        await _apiClient.CreateCoreV1NamespacedSecretAsync(body, Namespace);
    }

    public async Task<bool> ExistsSecretAsync(string secret)
    {
        try
        {
            await _apiClient.ReadCoreV1NamespacedSecretAsync(secret, Namespace);
            return true;
        }
        catch (Exception ex) when (IsResourceNotFound(ex))
        {
            return false;
        }
    }

    public async Task ApplyImageStreamAsync(string imageStream)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.ImageStream>(imageStream);
        try
        {
            await _apiClient.CreateImageOpenshiftIoV1NamespacedImageStreamAsync(body, Namespace);
        }
        catch (Exception ex) when (IsResourceExists(ex))
        {
            string name = body.Metadata.Name;

            // match the current resource version.
            var current = await _apiClient.ReadImageOpenshiftIoV1NamespacedImageStreamAsync(name, Namespace);
            body.Metadata.ResourceVersion = current.Metadata.ResourceVersion;

            await _apiClient.ReplaceImageOpenshiftIoV1NamespacedImageStreamAsync(body, name, Namespace);
        }
    }

    public async Task ApplyBuildConfigAsync(string buildConfig)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.BuildConfig>(buildConfig);
        try
        {
            await _apiClient.CreateBuildOpenshiftIoV1NamespacedBuildConfigAsync(body, Namespace);
        }
        catch (Exception ex) when (IsResourceExists(ex))
        {
            // TODO: update instead of deleting ...
            // Delete and re-create.
            string name = body.Metadata.Name;
            await _apiClient.DeleteBuildOpenshiftIoV1NamespacedBuildConfigAsync(name, Namespace);

            await _apiClient.CreateBuildOpenshiftIoV1NamespacedBuildConfigAsync(body, Namespace);
        }
    }

    public async Task ApplyRouteAsync(string route)
    {
        var body = JsonConvert.DeserializeObject<OpenShift.Route>(route);
        try
        {
            await _apiClient.CreateRouteOpenshiftIoV1NamespacedRouteAsync(body, Namespace);
        }
        catch (Exception ex) when (IsResourceExists(ex))
        {
            // TODO
        }
    }

    public async Task StartBinaryBuildAsync(string buildConfigName, Stream compressedApp)
    {
        await _apiClient.ConnectBuildOpenshiftIoV1PostNamespacedBuildConfigInstantiatebinaryAsync(buildConfigName, Namespace, new StreamContent(compressedApp));
    }

    public async Task StartBuildAsync(string buildConfigName)
    {
        OpenShift.BuildRequest body = new()
        {
            Metadata = new()
            {
                Name = buildConfigName
            }
        };
        await _apiClient.CreateBuildOpenshiftIoV1NamespacedBuildConfigInstantiateAsync(body, buildConfigName, Namespace);
    }

    private static bool IsResourceExists(Exception ex)
    {
        return ex is OpenShift.ApiException  { StatusCode: (int)(System.Net.HttpStatusCode.Conflict) };
    }

    private static bool IsResourceNotFound(Exception ex)
    {
        return ex is OpenShift.ApiException  { StatusCode: (int)(System.Net.HttpStatusCode.NotFound) };
    }

    private static List<Deployment> Map(List<OpenShift.DeploymentConfig> deployments)
        => deployments.Select(d => Map(d)).ToList();

    private static Deployment Map(OpenShift.DeploymentConfig deployment)
        => new Deployment
        {
            Name = deployment.Metadata.Name,
            Labels = new(deployment.Metadata.Labels)
        };

    private static User Map(OpenShift.User user)
        => new User
        {
            Name = user.Metadata.Name
        };

    private static Project Map(OpenShift.Project project)
        => new Project
        {
            Name = project.Metadata.Name
        };
}
