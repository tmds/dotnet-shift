namespace CommandHandlers;

using System.Net.Http;
using System.Security.Authentication;

sealed class LoginCommandHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private IOpenShiftClientFactory OpenShiftClientFactory { get; }
    private ILoginContextRepository KubeConfig { get; }

    public LoginCommandHandler(IAnsiConsole console, ILogger logger, IOpenShiftClientFactory clientFactory, ILoginContextRepository kubeConfig)
    {
        Console = console;
        Logger = logger;
        OpenShiftClientFactory = clientFactory;
        KubeConfig = kubeConfig;
    }

    public async Task<int> ExecuteAsync(string server, string token, bool skipVerify, string? name, string? ns, CancellationToken cancellationToken)
    {
        LoginContext login = new()
        {
            Name = name ?? "",
            Server = server,
            Token = token,
            SkipTlsVerify = skipVerify,
            Username = "",
            Namespace = ""
        };

        // Determine Username and SkipTlsVerify.
        IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);
        try
        {
            login.Username = await DetermineUserNameAsync(client, cancellationToken);
        }
        catch (HttpRequestException ex) when (!skipVerify && ex.InnerException is AuthenticationException)
        {
            // Can we log in skipping ssl verification?
            Debug.Assert(!login.SkipTlsVerify);

            login.SkipTlsVerify = true;
            client = OpenShiftClientFactory.CreateClient(login);
            try
            {
                login.Username = await DetermineUserNameAsync(client, cancellationToken);

                Console.WriteLine("The server uses a certificate signed by an unknown authority.");
                Console.WriteLine("You can bypass the certificate check, but any data you send to the server could be intercepted by others.");

                login.SkipTlsVerify = Console.Confirm("Use insecure connections?", defaultValue: false);

                if (!login.SkipTlsVerify)
                {
                    Console.WriteLine();
                    Console.WriteErrorLine($"The remote server is not trusted.");
                    return CommandResult.Failure;
                }
            }
            catch
            { }

            if (!login.SkipTlsVerify)
            {
                throw;
            }
            Debug.Assert(login.Username is not null);
        }
        Console.WriteLine($"Logged into '{server}' as '{login.Username}' using the provided token.");
        Console.WriteLine();

        // Determine Namespace.
        if (ns is not null)
        {
            login.Namespace = ns;
        }
        else
        {
            OpenShift.ProjectList projectList = await client.ListProjectsAsync(cancellationToken);
            List<OpenShift.Project> projects = projectList.Items;
            switch (projects.Count)
            {
                case 0:
                    Console.WriteErrorLine($"You don't have access to any namespaces on this cluster.");
                    return CommandResult.Failure;
                case 1:
                    login.Namespace = projects[0].Metadata.Name;
                    break;
                default:
                    if (!Console.Profile.Capabilities.Interactive)
                    {
                        Console.WriteLine("You have access to these namespaces:");
                        foreach (var project in projects)
                        {
                            Console.WriteLine($"  {project.Metadata.Name}");
                        }
                        Console.WriteLine();
                        Console.WriteErrorLine($"Cannot determine namespace. Specify the right namespace using the '{Cli.AppCommandLine.Options.NamespaceOption.Name}' option.");
                        return CommandResult.Failure;
                    }
                    login.Namespace = await new SelectionPrompt<string>()
                                            .Title("What namespace would you like to use?")
                                            .PageSize(10)
                                            .MoreChoicesText("[grey](Move up and down to reveal more namespaces)[/]")
                                            .AddChoices(projects.Select(project => project.Metadata.Name))
                                            .ShowAsync(Console, cancellationToken);
                    break;
            }
        }
        Console.WriteLine($"Using namespace '{login.Namespace}'.");

        // Store the login.
        KubeConfig.UpdateContext(login, setCurrent: true);

        return 0;
    }

    private static async Task<string> DetermineUserNameAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        var user = await client.GetUserAsync(cancellationToken);
        return user.Metadata.Name;
    }
}