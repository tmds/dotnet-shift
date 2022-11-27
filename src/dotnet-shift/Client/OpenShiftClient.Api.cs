using Task = System.Threading.Tasks.Task; // disambiguate with 'Task' class from the OpenShift client.

partial class OpenShiftClient
{
    private const string RuntimeLabel = "app.openshift.io/runtime";
    private const string Dotnet = "dotnet";

    private readonly OpenShift.OpenShiftApiClient _apiClient;

    public async Task<List<Deployment>> ListDeploymentsAsync()
    {
        var deploymentsList = await _apiClient.ListAppsV1NamespacedDeploymentAsync(Namespace);
        var deployments = Map(deploymentsList.Items);
        deployments = FilterDotnet(deployments);
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

    private static List<Deployment> Map(List<OpenShift.Deployment> deployments)
        => deployments.Select(d => Map(d)).ToList();

    private static Deployment Map(OpenShift.Deployment deployment)
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

    // This filters against 'app.openshift.io/runtime', so it will miss .NET deployments
    // that don't have this label set.
    private static List<T> FilterDotnet<T>(List<T> items) where T : IResource
        => items.Where(i => i.Labels.TryGetValue(RuntimeLabel, out var v) && v == Dotnet)
                .ToList();
}
