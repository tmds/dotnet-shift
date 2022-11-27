using System.Net.Http.Headers;

partial class OpenShiftClient
{
    public string Namespace { get; }

    public OpenShiftClient(string baseUrl, string token)
    {
        Namespace = "";
        _apiClient = CreateApiClient(baseUrl, token);
    }

    public OpenShiftClient()
    {
        var config = k8s.KubernetesClientConfiguration.BuildDefaultConfig();

        Namespace = config.Namespace;
 
        _apiClient = CreateApiClient(config.Host, config.AccessToken);
    }

    private OpenShift.OpenShiftApiClient CreateApiClient(string baseUrl, string token)
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new OpenShift.OpenShiftApiClient(baseUrl, httpClient);
    }
}