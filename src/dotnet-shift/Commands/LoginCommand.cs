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
