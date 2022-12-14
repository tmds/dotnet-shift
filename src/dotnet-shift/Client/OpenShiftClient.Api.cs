using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task; // disambiguate with 'Task' class from the OpenShift client.

partial class OpenShiftClient
{
    private const string RuntimeLabel = "app.openshift.io/runtime";
    private const string Dotnet = "dotnet";

    private readonly OpenShift.OpenShiftApiClient _apiClient;

    public async Task<List<Deployment>> ListDeploymentsAsync()
    {
        var deploymentsList = await _apiClient.ListAppsOpenshiftIoV1NamespacedDeploymentConfigAsync(Namespace, labelSelector: $"{ResourceLabels.Runtime}=dotnet,{ResourceLabels.PartOf}");
        var deployments = Map(deploymentsList.Items);
        return deployments;
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
        return projects.ToList();
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

    private static bool IsResourceExists(Exception ex)
    {
        return ex is OpenShift.ApiException  { StatusCode: (int)(System.Net.HttpStatusCode.Conflict) };
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
