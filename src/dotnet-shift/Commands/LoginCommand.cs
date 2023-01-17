using System.CommandLine;

sealed class LoginCommand : Command
{
    public LoginCommand() : base("login", "Log in to a server")
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
        /*
        oc login --token=sha256~l04fyDxcmOgKxZkLQll1EzofSgsdsfCuh8MtmSN9k80 --server=https://api.crc.testing:6443
The server uses a certificate signed by an unknown authority.
You can bypass the certificate check, but any data you send to the server could be intercepted by others.
Use insecure connections? (y/n): 

apiVersion: v1
clusters:
- cluster:
    insecure-skip-tls-verify: true
    server: https://api.crc.testing:6443
  name: api-crc-testing:6443
contexts:
- context:
    cluster: api-crc-testing:6443
    namespace: default
    user: kubeadmin/api-crc-testing:6443
  name: default/api-crc-testing:6443/kubeadmin
current-context: default/api-crc-testing:6443/kubeadmin
kind: Config
preferences: {}
users:
- name: kubeadmin/api-crc-testing:6443
  user:
    token: sha256~l04fyDxcmOgKxZkLQll1EzofSgsdsfCuh8MtmSN9k80

        */
        var client = new OpenShiftClient(server, token);
        string userName = await DetermineUserNameAsync(client);
        List<Project> projects = await client.ListProjectsAsync();

        Console.WriteLine($"Logged into '{server}' as 'userName' using the token provided.");
        Console.WriteLine();

        string ns;

        if (projects.Count > 0)
        {
            Console.WriteLine("You have access to the following namespaces and can switch between them with 'dotnet shift namespace set <NAMESPACE>':");
            Console.WriteLine();
            foreach (var project in projects)
            {
                Console.WriteLine($"    {project.Name}");
            }
            Console.WriteLine();

            ns = projects[0].Name;
        }
        else
        {
            Console.WriteLine("No accessible namespaces found.");
            Console.WriteLine("You can create a namespace using 'dotnet shift namespace create <NAMESPACE>':");
            Console.WriteLine();

            ns = "default";
        }
        Console.WriteLine($"Using namespace '{ns}'.");

        LoginContext context = new()
        {
            Server = server,
            UserName = userName,
            Namespace = ns,
            Token = token
        };

        KubernetesClientConfigFile.Update(context);
    }

    public static readonly Option<string> ServerOption =
        new Option<string>(new[] { "--server", "-s" }, "The address and port of the Kubernetes API server")
        { IsRequired = true };

    public static readonly Option<string> TokenOption =
        new Option<string>(new[] { "--token" }, "Bearer token for authentication to the API server")
        { IsRequired = true };
}
