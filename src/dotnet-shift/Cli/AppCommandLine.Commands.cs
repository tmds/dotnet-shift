using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using CommandHandlers;
using OpenShift;

namespace Cli;

partial class AppCommandLine
{
    private CliRootCommand CreateRootCommand()
    {
        CliRootCommand root = new()
        {
            Description = "A .NET tool for working with OpenShift"
        };

        // note: this is the order that appears in help.
        // 1. login
        root.Add(CreateLoginCommand());
        // 2. create
        root.Add(CreateDeployCommand());
        // 3. list
        root.Add(CreateListCommand());
        // 4. delete
        root.Add(CreateDeleteCommand());

        // other commands:
        root.Add(CreateContextCommand());

        root.SetAction(ctx =>
        {
            var Out = ctx.Configuration.Output;
            Out.WriteLine("'dotnet shift' is a .NET tool for working with OpenShift.");
            Out.WriteLine("");
            Out.WriteLine("You can use it to deploy a .NET project directly to OpenShift:");
            Out.WriteLine("");
            Out.WriteLine("- Log in to the OpenShift cluster using the 'login' command.");
            Out.WriteLine("  The token and server arguments can be found in the OpenShift Web console:");
            Out.WriteLine("");
            Out.WriteLine("   dotnet shift login --token=<TOKEN> --server=<SERVER>");
            Out.WriteLine("");
            Out.WriteLine("- Deploy the app to OpenShift using the 'deploy' command.");
            Out.WriteLine("  By adding the '--expose' option, the application is accessible externally.");
            Out.WriteLine("");
            Out.WriteLine("   dotnet deploy --expose <PATH TO PROJECT>");
            Out.WriteLine("");
            Out.WriteLine("For an overview of available commands, run 'dotnet shift --help'.");
        });

        return root;
    }

    private CliCommand CreateLoginCommand()
    {
        var command = CreateCommand("login", "Log in to a server");

        AddOptionsSorted(command,
                            Options.RequiredServerOption,
                            Options.RequiredTokenOption,
                            Options.InsecureSkipTlsVerifyOption,
                            Options.NamespaceOption,
                            Options.LoginNameOption);

        command.Handler = async (AppContext ctx, CancellationToken cancellationToken) =>
        {
            var services = ctx.Services;
            var parseResult = ctx.ParseResult;

            var handler = new LoginHandler(services.Console, services.Logger, services.OpenShiftClientFactory, services.KubeConfig);

            string server = parseResult.GetValue(Options.RequiredServerOption)!;
            string token = parseResult.GetValue(Options.RequiredTokenOption)!;
            string? ns = parseResult.GetValue(Options.NamespaceOption);
            string? name = parseResult.GetValue(Options.LoginNameOption);
            bool insecureSkipTlsVerifyOption = parseResult.GetValue(Options.InsecureSkipTlsVerifyOption);

            return await handler.ExecuteAsync(server, token, insecureSkipTlsVerifyOption, name, ns, cancellationToken);
        };

        return command;
    }

    private void AddOptionsSorted(CliCommand command, params CliOption[] options)
    {
        Array.Sort(options, (CliOption lhs, CliOption rhs) => lhs.Name.CompareTo(rhs.Name));
        foreach (var option in options)
        {
            command.Add(option);
        }
    }

    private System.CommandLine.CliCommand CreateContextCommand()
    {
        var command = new System.CommandLine.CliCommand("context", "Operate on connection contexts");

        // 1. get
        command.Add(CreateContextGetCommand());
        // 2. list
        command.Add(CreateContextListCommand());
        // 3. set
        command.Add(CreateContextSetCommand());
        // 4. delete
        command.Add(CreateContextDeleteCommand());

        return command;
    }

    private CliCommand CreateContextGetCommand()
    {
        var command = CreateCommand("get", "Print information about the current context");

        command.Add(Options.ContextOption);

        command.Handler = CreateHandlerBuilder()
                            .Filter(GetLoginContext)
                            .Handle(async (AppContext ctx, CancellationToken cancellationToken) =>
                            {
                                var services = ctx.Services;
                                var parseResult = ctx.ParseResult;

                                LoginContext loginContext = ctx.LoginContext!;

                                var handler = new ContextGetHandler(services.Console, services.Logger, services.KubeConfig);
                                return await handler.ExecuteAsync(loginContext, cancellationToken);
                            })
                            .Build();

        return command;
    }

    private CliCommand CreateContextListCommand()
    {
        var command = CreateCommand("list", "List connection contexts");

        command.Handler = async (AppContext ctx, CancellationToken cancellationToken) =>
        {
            var services = ctx.Services;
            var parseResult = ctx.ParseResult;

            var handler = new ContextListHandler(services.Console, services.Logger, services.KubeConfig);

            return await handler.ExecuteAsync(cancellationToken);
        };

        return command;
    }

    private CliCommand CreateContextSetCommand()
    {
        var command = CreateCommand("set", "Set the current connection context");

        command.Add(Options.RequiredContextArgument);

        command.Handler = async (AppContext ctx, CancellationToken cancellationToken) =>
        {
            var services = ctx.Services;
            var parseResult = ctx.ParseResult;

            var handler = new ContextSetHandler(services.Console, services.Logger, services.KubeConfig);

            string context = parseResult.GetValue(Options.RequiredContextArgument)!;

            return await handler.ExecuteAsync(context, cancellationToken);
        };

        return command;
    }

    private CliCommand CreateContextDeleteCommand()
    {
        var command = CreateCommand("delete", "Delete the specified connection context");

        command.Add(Options.RequiredContextArgument);

        command.Handler = async (AppContext ctx, CancellationToken cancellationToken) =>
        {
            var services = ctx.Services;
            var parseResult = ctx.ParseResult;

            var handler = new ContextDeleteHandler(services.Console, services.Logger, services.KubeConfig);

            string context = parseResult.GetValue(Options.RequiredContextArgument)!;

            return await handler.ExecuteAsync(context, cancellationToken);
        };

        return command;
    }

    private CliCommand CreateDeployCommand()
    {
        var command = CreateCommand("deploy", "Deploy a .NET application to OpenShift");

        AddOptionsSorted(command,
                            Options.DeploymentNameOption,
                            Options.ExposeOption,
                            Options.PartOfOption,
                            Options.NoFollowOption,
                            Options.NoBuildOption,
                            Options.ContextOption);

        command.Add(Options.RequiredDeployProjectArgument);

        command.Handler = CreateHandlerBuilder()
                            .Filter(GetLoginContext)
                            .Handle(async (AppContext ctx, CancellationToken cancellationToken) =>
                            {
                                var services = ctx.Services;
                                var parseResult = ctx.ParseResult;

                                var handler = new DeployHandler(services.Console, services.Logger, services.WorkingDirectory,
                                                    services.OpenShiftClientFactory, services.ProjectReader, services.GitRepoReader);

                                LoginContext loginContext = ctx.LoginContext!;

                                string project = parseResult.GetValue(Options.RequiredDeployProjectArgument)!;
                                bool expose = parseResult.GetValue(Options.ExposeOption);
                                bool noFollow = parseResult.GetValue(Options.NoFollowOption);
                                bool noBuild = parseResult.GetValue(Options.NoBuildOption);
                                string? partOf = parseResult.GetValue(Options.PartOfOption);
                                string? name = parseResult.GetValue(Options.DeploymentNameOption);

                                return await handler.ExecuteAsync(loginContext, project, name, partOf, expose, follow: !noFollow, doBuild: !noBuild, cancellationToken);
                            })
                            .Build();

        return command;
    }

    private CliCommand CreateDeleteCommand()
    {
        var command = CreateCommand("delete", "Delete an application");

        command.Add(Options.ContextOption);
        command.Add(Options.ForceOption);

        command.Add(Options.RequiredAppArgument);

        command.Handler = CreateHandlerBuilder()
                            .Filter(GetLoginContext)
                            .Handle(async (AppContext ctx, CancellationToken cancellationToken) =>
                            {
                                var services = ctx.Services;
                                var parseResult = ctx.ParseResult;

                                var handler = new DeleteHandler(services.Console, services.Logger, services.OpenShiftClientFactory);

                                LoginContext loginContext = ctx.LoginContext!;

                                string app = parseResult.GetValue(Options.RequiredAppArgument)!;
                                bool force = parseResult.GetValue(Options.ForceOption);

                                return await handler.ExecuteAsync(loginContext, app, force, cancellationToken);
                            })
                            .Build();

        return command;
    }

    private CliCommand CreateListCommand()
    {
        var command = CreateCommand("list", "List deployments");

        command.Add(Options.ContextOption);

        command.Handler = CreateHandlerBuilder()
                            .Filter(GetLoginContext)
                            .Handle(async (AppContext ctx, CancellationToken cancellationToken) =>
                            {
                                var services = ctx.Services;
                                var parseResult = ctx.ParseResult;

                                var handler = new ListHandler(services.Console, services.Logger, services.OpenShiftClientFactory);

                                LoginContext loginContext = ctx.LoginContext!;

                                return await handler.ExecuteAsync(loginContext, cancellationToken);
                            })
                            .Build();

        return command;
    }

    static async Task<int> GetLoginContext(AppContext ctx, Handler next, CancellationToken cancellationToken)
    {
        var services = ctx.Services;
        var parseResult = ctx.ParseResult;
        var Console = services.Console;

        string? context = parseResult.GetValue(Options.ContextOption);

        LoginContext? loginContext = context is null ? services.KubeConfig.GetCurrentContext()
                                                     : services.KubeConfig.GetAllContexts()
                                                                          .FirstOrDefault(c => c.Name == context);

        if (loginContext is null)
        {
            if (context is null)
            {
                Console.WriteErrorLine("There is no connection context.");
            }
            else
            {
                Console.WriteErrorLine($"Connection context '{context}' was not found.");
            }
            Console.WriteLine();
            Console.WriteLine("You can create a new connection using the 'login' command.");
            Console.WriteLine("You can list the available contexts using the 'context list' command, and select one using the 'context set' command.");
            return -1;
        }
        else
        {
            ctx.LoginContext = loginContext;

            return await next(ctx, cancellationToken);
        }
    }

    static async Task<int> ExceptionHandler(AppContext ctx, Handler next, CancellationToken cancellatinToken)
    {
        var services = ctx.Services;
        var Console = services.Console;
        Exception? printException = null;
        try
        {
            return await next(ctx, cancellatinToken);
        }
        catch (OperationCanceledException ce)
        {
            Console.WriteLine();
            Console.WriteErrorLine("The command was aborted by the user.");
            printException = ce;
        }
        catch (OpenShiftClientException clientException)
        {
            Console.WriteLine();
            // Print a human-friendly message for some errors.
            if (clientException.Cause == OpenShiftClientExceptionCause.ConnectionIssue)
            {
                if (clientException.SocketError.HasValue)
                {
                    switch (clientException.SocketError.Value)
                    {
                        case SocketError.HostNotFound:
                            Console.WriteErrorLine($"Host '{clientException.Host}' is not known.");
                            return CommandResult.Failure;
                    }
                }
            }

            switch (clientException.Cause)
            {
                case OpenShiftClientExceptionCause.ConnectionIssue:
                    Console.WriteErrorLine("There was an issue with the HTTP connection.");
                    break;
                case OpenShiftClientExceptionCause.UnexpectedResponseContent:
                    Console.WriteErrorLine("The server response was not understood.");
                    break;
                case OpenShiftClientExceptionCause.Failed:
                    if (clientException.HttpStatusCode == HttpStatusCode.Unauthorized)
                    {
                        Console.WriteErrorLine("The credentials are not valid for performing the requested operation.");
                        Console.WriteLine("Your credentials may have expired. You can use the 'login' command to update your credentials.");
                    }
                    else
                    {
                        Console.WriteErrorLine("The server failed to execute the request.");
                    }
                    break;
            }
            if (clientException.ResponseText is not null)
            {
                Console.WriteLine();
                Console.WriteLine("Server response:");
                Console.WriteLine(clientException.ResponseText.Trim(new[] { '\n', '\r' } ));
            }
            printException = clientException;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteErrorLine("An unexpected exception occurred while handling the command.");
            printException = ex;
        }

        if (printException is not null)
        {
            Console.WriteLine();
            Console.WriteLine("Exception stack trace:");
            Console.WriteException(printException);
            Console.WriteLine();
        }

        return CommandResult.Failure;
    }
}