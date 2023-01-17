using k8s;
using k8s.KubeConfigModels;
using System.Runtime.InteropServices;
using System.Text;

sealed class LoginContext
{
    public required string Server { get; set; }
    public required string Token { get; set; }
    public required string UserName { get; set; }
    public required string Namespace { get; set; }
}

static class KubernetesClientConfigFile
{
    public static LoginContext GetDefaultContext()
    {
        var config = k8s.KubernetesClientConfiguration.BuildDefaultConfig();

        return new LoginContext
        {
            Server = config.Host,
            Token = config.AccessToken,
            UserName = config.Username,
            Namespace = config.Namespace
        };
    }

    public static void Update(LoginContext ctx)
    {
        string server = ctx.Server;
        string token = ctx.Token;
        string userName = ctx.UserName;
        string ns = ctx.Namespace;

        Uri serverUri = new Uri(server); // TODO: handle invalid uri format.
        string clusterId = $"{serverUri.Host.Replace('.', '-')}:{serverUri.Port}";
        string userId = $"{userName}/{clusterId}";
        string contextId = $"{ns}/{clusterId}/{userName}";

        Cluster cluster = new()
        {
            Name = clusterId,
            ClusterEndpoint = new()
            {
                Server = server
            }
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

        var configFilePath = KubernetesClientConfiguration.KubeConfigDefaultLocation;
        if (File.Exists(configFilePath))
        {
            UpdateConfigfile(configFilePath, cluster, context, user);
        }
        else
        {
            CreateConfigfile(configFilePath, cluster, context, user);
        }
    }

    private static void CreateConfigfile(string configFilePath, Cluster cluster, Context context, k8s.KubeConfigModels.User user)
    {
        // Ensure directory exists.
        string directory = Path.GetDirectoryName(configFilePath)!;
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
        WriteConfigFile(configFilePath, config);
    }

    private static void WriteConfigFile(string configFilePath, K8SConfiguration config)
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
            if (!File.Exists(configFilePath))
            {
                // temp files have user-only access.
                string userFile = Path.GetTempFileName();
                File.Move(userFile, configFilePath);
            }
#else
            fso.UnixCreateMode = UnixFileMode.UserWrite | UnixFileMode.UserRead;
#endif
        }

        using var fs = new FileStream(configFilePath, fso);
        fs.Write(Encoding.UTF8.GetBytes(KubernetesYaml.Serialize(config)));
    }

    private static void UpdateConfigfile(string configFilePath, Cluster cluster, Context context, k8s.KubeConfigModels.User user)
    {
        K8SConfiguration config = KubernetesClientConfiguration.LoadKubeConfig(configFilePath);
        config.CurrentContext = context.Name;
        config.Contexts = AppendToList(config.Contexts, context, context => context.Name);
        config.Users = AppendToList(config.Users, user, user => user.Name);
        config.Clusters = AppendToList(config.Clusters, cluster, cluster => cluster.Name);

        WriteConfigFile(configFilePath, config);

        static IEnumerable<T> AppendToList<T>(IEnumerable<T>? items, T item, Func<T, string> getName)
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
}