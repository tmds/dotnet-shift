using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;

partial class OpenShiftClient
{
    public string Namespace { get; }

    public OpenShiftClient() : this(KubernetesClientConfigFile.GetDefaultContext())
    { }

    public OpenShiftClient(LoginContext login)
    {
        Namespace = login.Namespace;
 
        _apiClient = CreateApiClient(login.Server, login.Token, login.SkipTlsVerify);
    }

    private OpenShift.OpenShiftApiClient CreateApiClient(string baseUrl, string token, bool skipTlsVerify)
    {
        var httpClient = new HttpClient(new MessageHandler(skipTlsVerify));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new OpenShift.OpenShiftApiClient(baseUrl, httpClient);
    }

    private class MessageHandler : DelegatingHandler
    {
        public MessageHandler(bool skipTlsVerify) : base(CreateBaseHandler(skipTlsVerify))
        { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content?.Headers?.ContentType?.MediaType == "*/*")
            {
                // OpenShift needs to know what the content really is.
                request.Content.Headers.ContentType.MediaType = "application/json";
            }
            if (request.Method == HttpMethod.Patch)
            {
                request.Content.Headers.ContentType.MediaType = "application/strategic-merge-patch+json";
            }
            return base.SendAsync(request, cancellationToken);
        }

        private static HttpMessageHandler CreateBaseHandler(bool skipTlsVerify)
        {
            var handler = new SocketsHttpHandler();

            if (skipTlsVerify)
            {
                handler.SslOptions = new SslClientAuthenticationOptions()
                {
                    // TODO: only ignore unknown root?
                    RemoteCertificateValidationCallback = delegate { return true; },
                };
            }

            return handler;
        }
    }
}