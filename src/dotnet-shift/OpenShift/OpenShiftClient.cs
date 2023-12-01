namespace OpenShift;

using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;

#nullable disable

partial class OpenShiftClient : IOpenShiftClient
{
    public string Namespace { get; }

    private bool SkipTlsVerify { get; }
    private string BaseUrl { get; }
    private string Token { get; }
    private X509Certificate2Collection CACerts { get; }

    public OpenShiftClient(string server, string token, string @namespace, bool skipTlsVerify, X509Certificate2Collection caCerts)
    {
        BaseUrl = server;
        Namespace = @namespace;
        SkipTlsVerify = skipTlsVerify;
        Token = token;
        CACerts = caCerts;
        _settings = new Lazy<Newtonsoft.Json.JsonSerializerSettings>(CreateSerializerSettings);
        _httpClient = new HttpClient(new MessageHandler(token, skipTlsVerify, caCerts, Host));
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    private Newtonsoft.Json.JsonSerializerSettings CreateSerializerSettings()
    {
        var settings = new Newtonsoft.Json.JsonSerializerSettings();
        UpdateJsonSerializerSettings(settings);
        return settings;
    }

    private OpenShiftClientException CreateApiException(string message, int statusCode, string response, IReadOnlyDictionary<string, IEnumerable<string>> headers, Exception innerException)
    {
        // NSwag does 'throw new ApiException' which we search and replace by 'throw CreateApiException'.
        // Here we map the arguments to what we want for OpenShiftClientException.

        return new OpenShiftClientException(Host, UpdateMessage(message, statusCode), GetCause(message), GetStatusCode(statusCode, message), response, innerException);

        static OpenShiftClientExceptionCause GetCause(string message) =>
            message == "Response was null which was not expected." ? OpenShiftClientExceptionCause.UnexpectedResponseContent : OpenShiftClientExceptionCause.Failed;

        static System.Net.HttpStatusCode? GetStatusCode(int statusCode, string message)
            => GetCause(message) == OpenShiftClientExceptionCause.UnexpectedResponseContent ? null : (System.Net.HttpStatusCode)statusCode;

        static string UpdateMessage(string message, int statusCode)
        {
            if (message.StartsWith("The HTTP status code of the response was not expected ("))
            {
                message = $"The HTTP status code of the response was not expected: {(System.Net.HttpStatusCode)statusCode} ({statusCode})";
            }
            return message;
        }
    }

    private class MessageHandler : DelegatingHandler
    {
        private readonly AuthenticationHeaderValue _authHeader;
        private readonly string _host;

        public MessageHandler(string token, bool skipTlsVerify, X509Certificate2Collection caCerts, string host) : base(CreateBaseHandler(skipTlsVerify, caCerts))
        {
            _authHeader = new AuthenticationHeaderValue("Bearer", token);
            _host = host;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = _authHeader;

            if (request.Content?.Headers?.ContentType?.MediaType is string mediaType)
            {
                Debug.Assert(mediaType != "*/*" &&                        // OpenShift wants the exact type, like "application/json".
                             mediaType != "application/json-patch+json"); // Use stragetic merge: "application/strategic-merge-patch+json"
            }

            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new OpenShiftClientException(_host, ex.Message, OpenShiftClientExceptionCause.ConnectionIssue, httpStatusCode: null, responseText: null, ex);
            }
        }

        private static HttpMessageHandler CreateBaseHandler(bool skipTlsVerify, X509Certificate2Collection caCerts)
        {
            var handler = new SocketsHttpHandler();

            if (skipTlsVerify)
            {
                handler.SslOptions = new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback = delegate { return true; },
                };
            }
            else if (caCerts is not null)
            {
                handler.SslOptions = new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback =
                        (sender, certificate, chain, sslPolicyErrors) => ValidateCertificate(sender, caCerts, certificate, chain, sslPolicyErrors),
                };
            }

            return handler;
        }
    }

    private System.Net.Http.HttpClient _httpClient;
    private Lazy<Newtonsoft.Json.JsonSerializerSettings> _settings;

    private Newtonsoft.Json.JsonSerializerSettings JsonSerializerSettings { get { return _settings.Value; } }

    partial void UpdateJsonSerializerSettings(Newtonsoft.Json.JsonSerializerSettings settings);

    internal string Host
    {
        get
        {
            Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri uri);
            return uri?.Host ?? "";
        }
    }

    private string ConvertToString(object value, System.Globalization.CultureInfo cultureInfo)
    {
        if (value == null)
        {
            return "";
        }

        if (value is System.Enum)
        {
            var name = System.Enum.GetName(value.GetType(), value);
            if (name != null)
            {
                var field = System.Reflection.IntrospectionExtensions.GetTypeInfo(value.GetType()).GetDeclaredField(name);
                if (field != null)
                {
                    var attribute = System.Reflection.CustomAttributeExtensions.GetCustomAttribute(field, typeof(System.Runtime.Serialization.EnumMemberAttribute))
                        as System.Runtime.Serialization.EnumMemberAttribute;
                    if (attribute != null)
                    {
                        return attribute.Value != null ? attribute.Value : name;
                    }
                }

                var converted = System.Convert.ToString(System.Convert.ChangeType(value, System.Enum.GetUnderlyingType(value.GetType()), cultureInfo));
                return converted == null ? string.Empty : converted;
            }
        }
        else if (value is bool)
        {
            return System.Convert.ToString((bool)value, cultureInfo).ToLowerInvariant();
        }
        else if (value is byte[])
        {
            return System.Convert.ToBase64String((byte[])value);
        }
        else if (value.GetType().IsArray)
        {
            var array = System.Linq.Enumerable.OfType<object>((System.Array)value);
            return string.Join(",", System.Linq.Enumerable.Select(array, o => ConvertToString(o, cultureInfo)));
        }

        var result = System.Convert.ToString(value, cultureInfo);
        return result == null ? "" : result;
    }

    private async Task<ObjectResponseResult<T>> ReadObjectResponseAsync<T>(System.Net.Http.HttpResponseMessage response, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> headers, System.Threading.CancellationToken cancellationToken)
    {
        if (response == null || response.Content == null)
        {
            return new ObjectResponseResult<T>(default(T), string.Empty);
        }

        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            var typedBody = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(responseText, JsonSerializerSettings);
            return new ObjectResponseResult<T>(typedBody, responseText);
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            var message = "Could not deserialize the response body string as " + typeof(T).FullName + ".";
            throw new OpenShiftClientException(Host, message, OpenShiftClientExceptionCause.UnexpectedResponseContent, httpStatusCode: null, responseText, exception);
        }
    }

    private ClientWebSocket CreateWebSocket(string subProtocol)
    {
        ClientWebSocket webSocket = new ClientWebSocket();
        webSocket.Options.AddSubProtocol(subProtocol);
        webSocket.Options.SetRequestHeader("Authorization", $"Bearer {Token}");
        if (SkipTlsVerify)
        {
            webSocket.Options.RemoteCertificateValidationCallback = delegate { return true; };
        }
        if (CACerts is not null)
        {
            webSocket.Options.RemoteCertificateValidationCallback =
                (sender, certificate, chain, sslPolicyErrors) => ValidateCertificate(sender, CACerts, certificate, chain, sslPolicyErrors);
        }
        return webSocket;
    }

    private string WebSocketBaseUrl
    {
        get
        {
            string baseUrl = BaseUrl.TrimEnd('/');
            baseUrl = baseUrl.Replace("https://", "wss://");
            baseUrl = baseUrl.Replace("http://", "ws://");
            return baseUrl;
        }
    }

    struct ObjectResponseResult<T>
    {
        public ObjectResponseResult(T responseObject, string responseText)
        {
            this.Object = responseObject;
            this.Text = responseText;
        }

        public T Object { get; }

        public string Text { get; }
    }

    partial void PrepareRequest(System.Net.Http.HttpClient client, System.Net.Http.HttpRequestMessage request, string url);
    partial void PrepareRequest(System.Net.Http.HttpClient client, System.Net.Http.HttpRequestMessage request, System.Text.StringBuilder urlBuilder);
    partial void ProcessResponse(System.Net.Http.HttpClient client, System.Net.Http.HttpResponseMessage response);

    internal static bool ValidateCertificate(object sender, X509Certificate2Collection caCerts, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ExtraStore.AddRange(caCerts);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            bool isValid = chain.Build((X509Certificate2)certificate);
            if (!isValid)
            {
                return false;
            }

            foreach (var caCert in caCerts)
            {
                bool isTrusted = chain.Build(caCert);
                if (isTrusted)
                {
                    return true;
                }
            }
        }

        return false;
    }
}