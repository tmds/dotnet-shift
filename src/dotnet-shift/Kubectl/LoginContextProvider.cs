using System.Security.Cryptography.X509Certificates;

namespace Kubectl;

sealed class LoginContextProvider : ILoginContextProvider
{
    private readonly ILoginContextRepository? _configFile;

    public LoginContextProvider(ILoginContextRepository? configFile)
    {
        _configFile = configFile;
    }

    public LoginContext? GetContext(string? name)
    {
        if (name is not null)
        {
            return _configFile?.GetAllContexts()?.FirstOrDefault(c => c.Name == name);
        }

        return _configFile?.GetCurrentContext() ?? GetServiceAccountLogin();
    }

    private static LoginContext? GetServiceAccountLogin()
    {
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port))
        {
            return null;
        }

        const string ServiceAccountPath = "/var/run/secrets/kubernetes.io/serviceaccount";
        const string TokenPath = $"{ServiceAccountPath}/token";
        const string NamespacePath = $"{ServiceAccountPath}/namespace";
        const string CACertPath = $"{ServiceAccountPath}/ca.crt";
        if (!File.Exists(TokenPath) || !File.Exists(NamespacePath))
        {
            return null;
        }

        string server = new UriBuilder("https", host, Convert.ToInt32(port)).ToString();

        X509Certificate2Collection caCerts = new();
        caCerts.ImportFromPemFile(CACertPath);

        return new LoginContext()
        {
            Name = "Kubernetes",
            Server = server,
            Token = File.ReadAllText(TokenPath),
            Username = "<serviceaccount>",
            Namespace = File.ReadAllText(NamespacePath),
            CACerts = caCerts,
            SkipTlsVerify = false
        };
    }
}