using System.Net.Http.Headers;

partial class OpenShiftClient
{
    public string Namespace { get; }

    public OpenShiftClient()
    {
        var config = k8s.KubernetesClientConfiguration.BuildDefaultConfig();

        Namespace = config.Namespace;
 
        _apiClient = CreateApiClient(config);
    }

    private OpenShift.OpenShiftApiClient CreateApiClient(k8s.KubernetesClientConfiguration config)
    {
        string baseUrl = config.Host;
        string token = config.AccessToken;

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
        return new OpenShift.OpenShiftApiClient(config.Host, httpClient);
    }
}