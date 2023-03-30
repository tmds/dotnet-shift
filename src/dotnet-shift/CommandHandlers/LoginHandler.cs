namespace CommandHandlers;

using OpenShift;
using System.Net.Http;
using System.Security.Authentication;

sealed class LoginHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private IOpenShiftClientFactory OpenShiftClientFactory { get; }
    private ILoginContextRepository KubeConfig { get; }

    public LoginHandler(IAnsiConsole console, ILogger logger, IOpenShiftClientFactory clientFactory, ILoginContextRepository kubeConfig)
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
        catch (OpenShiftClientException ex) when (!login.SkipTlsVerify && ex.AuthenticationException is { } authException)
        {
            Console.WriteLine($"The connection is not trusted: {authException.Message}.");
            Console.WriteLine("You can bypass the certificate check, but any data you send to the server could be intercepted by others.");
            Console.WriteLine();

            if (!Console.Profile.Capabilities.Interactive)
            {
                Console.WriteErrorLine($"Cannot log in to an insecure server. To ignore the certificate, you can add the '{Cli.AppCommandLine.Options.InsecureSkipTlsVerifyOption.Name}' option.");
                return CommandResult.Failure;
            }

            login.SkipTlsVerify = await new ConfirmationPrompt("Use insecure connections?") { DefaultValue = false }
                                            .ShowAsync(Console, cancellationToken);
            Console.WriteLine();

            if (!login.SkipTlsVerify)
            {
                Console.WriteErrorLine($"The remote server is not trusted.");
                return CommandResult.Failure;
            }

            client = OpenShiftClientFactory.CreateClient(login);
            login.Username = await DetermineUserNameAsync(client, cancellationToken);
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
                    Console.WriteLine($"You have access to a single namespace: '{login.Namespace}'.");
                    Console.WriteLine();
                    break;
                default:
                    if (!Console.Profile.Capabilities.Interactive)
                    {
                        Console.WriteErrorLine($"Cannot determine namespace. Specify the right namespace using the '{Cli.AppCommandLine.Options.NamespaceOption.Name}' option.");
                        return CommandResult.Failure;
                    }
                    var namespacePrompt = new SelectionPrompt<string>()
                                            .Title("What namespace would you like to use?")
                                            .PageSize(10)
                                            .MoreChoicesText("[grey](Move up and down to reveal more namespaces)[/]")
                                            .AddChoices(projects.Select(project => project.Metadata.Name));
                    login.Namespace = await namespacePrompt
                                            .ShowAsync(Console, cancellationToken);

                    // Print again to show what namespace was selected.
                    Console.WriteLine($"{namespacePrompt.Title} {login.Namespace}");
                    Console.WriteLine();
                    break;
            }
        }

        List<LoginContext> contexts = KubeConfig.GetAllContexts(includeTokens: false);

        if (string.IsNullOrEmpty(login.Name))
        {
            if (Console.Profile.Capabilities.Interactive)
            {
                // Try to find back a name that was previously used for this connection.
                LoginContext? match = contexts.Where(c => c.Namespace == login.Namespace &&
                                                        c.Server == login.Server &&
                                                        c.Username == login.Username &&
                                                        c.SkipTlsVerify == login.SkipTlsVerify)
                                            .OrderBy(c => c.Name.Length)
                                            .FirstOrDefault();
                string defaultName = match?.Name ?? KubeConfig.GetDefaultName(login);

                login.Name = await new TextPrompt<string>("How would you like to name this context?")
                                .DefaultValue(defaultName)
                                .ShowAsync(Console, cancellationToken);
                Console.WriteLine();
            }
            else
            {
                login.Name = KubeConfig.GetDefaultName(login);
            }
        }

        // Store the login.
        bool isUpdate = contexts.Any(c => c.Name == login.Name);
        KubeConfig.UpdateContext(login, setCurrent: true);
        Console.WriteLine($"The login context '{login.Name}' was {(isUpdate ? "updated" : "added")}.");
        Console.WriteLine();

        System.Uri.TryCreate(login.Server, System.UriKind.Absolute, out var uri);
        Console.WriteLine($"Using namespace '{login.Namespace}' on server '{uri?.Host}'.");

        return 0;
    }

    private static async Task<string> DetermineUserNameAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        var user = await client.GetUserAsync(cancellationToken);
        return user.Metadata.Name;
    }
}