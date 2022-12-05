using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;
using k8s;
using k8s.KubeConfigModels;

sealed class LoginCommand : Command
{
    public LoginCommand() : base("login")
    {
        AddOption(ServerOption);
        AddOption(TokenOption);

        this.SetHandler((string server, string token) => HandleAsync(server, token), ServerOption, TokenOption);
    }

    private static async Task<string> DetermineUserNameAsync(OpenShiftClient client)
    {
        var user = await client.GetUserAsync();
        return user.Name;
    }

    private static async Task<string> DetermineNamespaceAsync(OpenShiftClient client)
    {
        // TODO: handle no projects, Kubernetes, prefer project known in ~/.kube/config.
        var projects = await client.ListProjectsAsync();
        return projects[0].Name;
    }

    public static async Task HandleAsync(string server, string token)
    {
        var client = new OpenShiftClient(server, token);
        string userName = await DetermineUserNameAsync(client);
        string ns = await DetermineNamespaceAsync(client);

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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fso.UnixCreateMode = UnixFileMode.UserWrite | UnixFileMode.UserRead;
        }
        using var fs = new FileStream(configFilePath, fso);
        fs.Write(Encoding.UTF8.GetBytes(KubernetesYaml.Serialize(config)));
    }

    public static readonly Option<string> ServerOption =
        new Option<string>(new[] { "--server", "-s" }, "The address and port of the Kubernetes API server")
        { IsRequired = true };

    public static readonly Option<string> TokenOption =
        new Option<string>(new[] { "--token" }, "Bearer token for authentication to the API server")
        { IsRequired = true };
}
