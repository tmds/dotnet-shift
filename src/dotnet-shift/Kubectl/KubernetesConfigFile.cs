namespace Kubectl;

using k8s;
using k8s.KubeConfigModels;
using System.Runtime.InteropServices;
using Uri = System.Uri;

sealed class KubernetesConfigFile : ILoginContextRepository
{
    private readonly string _configFilePath;

    public KubernetesConfigFile() :
        this(KubernetesClientConfiguration.KubeConfigDefaultLocation)
    { }

    public KubernetesConfigFile(string configFilePath)
    {
        _configFilePath = configFilePath;
    }

    public LoginContext? GetCurrentContext()
    {
        if (!File.Exists(_configFilePath))
        {
            return null;
        }

        K8SConfiguration config = KubernetesClientConfiguration.LoadKubeConfig(_configFilePath);

        string contextID = config.CurrentContext;
        Context? context = config.Contexts.FirstOrDefault(c => c.Name == contextID);
        if (context is null)
        {
            return null;
        }

        return KubeContextToLoginContext(config, context, includeToken: true);
    }

    public void UpdateContext(LoginContext loginContext, bool setCurrent)
    {
        string server = loginContext.Server;
        string token = loginContext.Token;
        string userName = loginContext.Username;
        string ns = loginContext.Namespace;

        Uri serverUri = new Uri(server); // TODO: handle invalid uri format.
        string clusterId = $"{serverUri.Host.Replace('.', '-')}:{serverUri.Port}";
        string userId = $"{userName}/{clusterId}";
        string contextId = loginContext.Name;
        if (string.IsNullOrEmpty(contextId))
        {
            contextId = $"{ns}/{clusterId}/{userName}";
        }

        Cluster cluster = new()
        {
            Name = clusterId,
            ClusterEndpoint = new()
            {
                Server = server,
                SkipTlsVerify = loginContext.SkipTlsVerify
            },
        };
        Context context = new()
        {
            Name = contextId,
            ContextDetails = new() { Cluster = clusterId, User = userId, Namespace = ns }
        };
        k8s.KubeConfigModels.User user = new()
        {
            Name = userId,
            UserCredentials = new() { Token = token }
        };

        if (File.Exists(_configFilePath))
        {
            UpdateConfigfile(_configFilePath, cluster, context, user, setCurrent);
        }
        else
        {
            CreateConfigfile(_configFilePath, cluster, context, user);
        }
    }

    private static void CreateConfigfile(string _configFilePath, Cluster cluster, Context context, k8s.KubeConfigModels.User user)
    {
        // Ensure directory exists.
        string directory = Path.GetDirectoryName(_configFilePath)!;
        Directory.CreateDirectory(directory);

        K8SConfiguration config = new()
        {
            ApiVersion = "v1",
            Kind = "Config",
            CurrentContext = context.Name,
            Contexts = new[] { context },
            Users = new[] { user },
            Clusters = new[] { cluster }
        };
        WriteConfigFile(_configFilePath, config);
    }

    private static void WriteConfigFile(string _configFilePath, K8SConfiguration config)
    {
        FileStreamOptions fso = new()
        {
            Access = FileAccess.Write,
            Mode = FileMode.Create, // Create new or truncate existing.
        };

        // Make the config file rw for the user only.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if NET6_0
            if (!File.Exists(_configFilePath))
            {
                // temp files have user-only access.
                string userFile = Path.GetTempFileName();
                File.Move(userFile, _configFilePath);
            }
#else
            fso.UnixCreateMode = UnixFileMode.UserWrite | UnixFileMode.UserRead;
#endif
        }

        using var fs = new FileStream(_configFilePath, fso);
        fs.Write(Encoding.UTF8.GetBytes(KubernetesYaml.Serialize(config)));
    }

    private static void UpdateConfigfile(string _configFilePath, Cluster cluster, Context context, k8s.KubeConfigModels.User user, bool setCurrent)
    {
        K8SConfiguration config = KubernetesClientConfiguration.LoadKubeConfig(_configFilePath);
        if (setCurrent)
        {
            config.CurrentContext = context.Name;
        }
        config.Contexts = AppendToList(config.Contexts, context, context => context.Name);
        config.Users = AppendToList(config.Users, user, user => user.Name);
        config.Clusters = AppendToList(config.Clusters, cluster, cluster => cluster.Name);

        WriteConfigFile(_configFilePath, config);

        static IEnumerable<T> AppendToList<T>(IEnumerable<T>? items, T item, System.Func<T, string> getName)
        {
            List<T> merged = new();
            if (items is not null)
            {
                foreach (var i in items)
                {
                    if (getName(i) != getName(item))
                    {
                        merged.Add(i);
                    }
                }
            }
            merged.Add(item);
            return merged;
        }
    }

    public List<LoginContext> GetAllContexts(bool includeTokens)
    {
        List<LoginContext> logins = new();

        if (!File.Exists(_configFilePath))
        {
            return logins;
        }

        K8SConfiguration config = KubernetesClientConfiguration.LoadKubeConfig(_configFilePath);

        foreach (var context in config.Contexts)
        {
            LoginContext? login = KubeContextToLoginContext(config, context, includeTokens);

            if (login is not null)
            {
                logins.Add(login);
            }
        }

        return logins;
    }

    private static LoginContext? KubeContextToLoginContext(K8SConfiguration config, Context context, bool includeToken)
    {
        string ns = context.ContextDetails.Namespace;
        string contextId = context.Name;
        string clusterId = context.ContextDetails.Cluster;
        string userId = context.ContextDetails.User;

        Cluster? cluster = config.Clusters.FirstOrDefault(c => c.Name == clusterId);
        string? server = cluster?.ClusterEndpoint.Server;
        bool skipTlsVerify = cluster?.ClusterEndpoint.SkipTlsVerify ?? false;

        string userName = userId;
        if (userName.Contains('/'))
        {
            userName = userName.Substring(0, userName.IndexOf('/'));
        }

        string token = "<secret>"; // keep the real token secret.
        if (includeToken)
        {
            k8s.KubeConfigModels.User? user = config.Users.FirstOrDefault(u => u.Name == userId);
            token = user?.UserCredentials.Token ?? "";
        }

        LoginContext? login = null;

        if (server != null && userName != null && ns != null)
        {
            login = new LoginContext()
            {
                Name = contextId,
                Server = server,
                Token = token,
                Username = userName,
                Namespace = ns,
                SkipTlsVerify = skipTlsVerify
            };
        }

        return login;
    }

    public void SetCurrentContext(string contextName)
    {
        K8SConfiguration config = KubernetesClientConfiguration.LoadKubeConfig(_configFilePath);
        config.CurrentContext = contextName;
        WriteConfigFile(_configFilePath, config);
    }

    public bool DeleteContext(string contextName)
    {
        if (!File.Exists(_configFilePath))
        {
            return false;
        }

        K8SConfiguration config = KubernetesClientConfiguration.LoadKubeConfig(_configFilePath);
        Context? context = config.Contexts.FirstOrDefault(c => c.Name == contextName);
        if (context is null)
        {
            return false;
        }

        config.Contexts = config.Contexts.Where(c => c != context);

        string clusterId = context.ContextDetails.Cluster;
        bool clusterHasOtherContexts = config.Contexts.Count(c => c.ContextDetails.Cluster == clusterId) > 1;
        if (!clusterHasOtherContexts)
        {
            config.Clusters = config.Clusters.Where(c => c.Name != clusterId);
        }

        string userId = context.ContextDetails.User;
        bool userHasOtherContexts = config.Contexts.Count(c => c.ContextDetails.User == userId) > 1;
        if (!userHasOtherContexts)
        {
            config.Users = config.Users.Where(u => u.Name != userId);
        }

        if (config.CurrentContext == contextName)
        {
            config.CurrentContext = null;
        }
        WriteConfigFile(_configFilePath, config);

        return true;
    }

    public LoginContext? GetContext(string contextName)
        => GetAllContexts(includeTokens: true).FirstOrDefault(c => c.Name == contextName);
}