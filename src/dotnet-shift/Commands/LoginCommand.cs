using System.CommandLine;
using System.Diagnostics;
using System.Security.Authentication;

sealed class LoginCommand : Command
{
    public LoginCommand() : base("login", "Log in to a server")
    {
        Add(ServerOption);
        Add(TokenOption);
        Add(InsecureSkipTlsVerifyOption);

        this.SetHandler((string server, string token, bool skipVerify) => HandleAsync(server, token, skipVerify), ServerOption, TokenOption, InsecureSkipTlsVerifyOption);
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

    public static async Task<int> HandleAsync(string server, string token, bool skipVerify)
    {
        LoginContext login = new()
        {
            Server = server,
            Token = token,
            SkipTlsVerify = skipVerify,
            UserName = "",
            Namespace = ""
        };
        var client = new OpenShiftClient(login);
        string? userName = null;
        try
        {
            userName = await DetermineUserNameAsync(client);
        }
        catch (HttpRequestException ex) when (!skipVerify && ex.InnerException is AuthenticationException)
        {
            Debug.Assert(!skipVerify);

            // Can we log in skipping ssl verification
            login.SkipTlsVerify = true;
            client = new OpenShiftClient(login);
            try
            {
                userName = await DetermineUserNameAsync(client);

                Console.WriteLine("The server uses a certificate signed by an unknown authority.");
                Console.WriteLine("You can bypass the certificate check, but any data you send to the server could be intercepted by others.");
                while (true)
                {
                    Console.Write("Use insecure connections? (y/n): ");
                    string? answer = Console.ReadLine();
                    answer ??= "no";

                    if (answer == "yes" || answer == "y")
                    {
                        skipVerify = true;
                        break;
                    }
                    else if (answer == "no" || answer == "n")
                    {
                        Console.WriteLine();
                        Console.Error.WriteLine($"error: The remote server is not trusted.");
                        return 1;
                    }
                    else
                    {
                        System.Console.WriteLine("Please enter 'yes' or 'no'.");
                    }
                }
            }
            catch
            { }

            if (!skipVerify)
            {
                throw;
            }
            Debug.Assert(userName is not null);
        }

        List<Project> projects = await client.ListProjectsAsync();

        Console.WriteLine($"Logged into '{server}' as '{userName}' using the token provided.");
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
            Token = token,
            SkipTlsVerify = skipVerify
        };

        KubernetesClientConfigFile.Update(context);

        return 0;
    }

    public static readonly Option<string> ServerOption =
        new Option<string>(new[] { "--server", "-s" }, "The address and port of the Kubernetes API server")
        { IsRequired = true };

    public static readonly Option<string> TokenOption =
        new Option<string>(new[] { "--token" }, "Bearer token for authentication to the API server")
        { IsRequired = true };

    public static readonly Option<bool> InsecureSkipTlsVerifyOption =
        new Option<bool>(new[] { "--insecure-skip-tls-verify" }, "If true, the server's certificate will not be checked for validity (insecure)")  { Arity = ArgumentArity.Zero };
}
