using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using CommandHandlers;
using OpenShift;

namespace Cli;

partial class AppCommandLine
{
    private RootCommand CreateRootCommand()
    {
        RootCommand root = new();
        root.Add(CreateLoginCommand());
        root.Add(CreateDeployCommand());
        root.Add(CreateContextCommand());
        root.Add(CreateListCommand());
        root.Add(CreateDeleteCommand());

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

    private Command CreateLoginCommand()
    {
        var command = CreateCommand("login", "Log in to a server");

        command.Add(Options.RequiredServerOption);
        command.Add(Options.RequiredTokenOption);
        command.Add(Options.InsecureSkipTlsVerifyOption);
        command.Add(Options.NamespaceOption);
        command.Add(Options.LoginNameOption);

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

    private System.CommandLine.Command CreateContextCommand()
    {
        var command = new System.CommandLine.Command("context", "Operate on connection contexts");

        command.Add(CreateContextGetCommand());
        command.Add(CreateContextListCommand());
        command.Add(CreateContextSetCommand());
        command.Add(CreateContextDeleteCommand());

        return command;
    }

    private Command CreateContextGetCommand()
    {
        var command = CreateCommand("get", "Print information about the current context");

        command.Handler = async (AppContext ctx, CancellationToken cancellationToken) =>
        {
            var services = ctx.Services;
            var parseResult = ctx.ParseResult;

            var handler = new ContextGetHandler(services.Console, services.Logger, services.KubeConfig);
            return await handler.ExecuteAsync(cancellationToken);
        };

        return command;
    }

    private Command CreateContextListCommand()
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

    private Command CreateContextSetCommand()
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

    private Command CreateContextDeleteCommand()
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

    private Command CreateDeployCommand()
    {
        var command = CreateCommand("deploy", "Deploy a .NET application to OpenShift");

        command.Add(Options.RequiredDeployProjectArgument);
        command.Add(Options.DeploymentNameOption);
        command.Add(Options.ExposeOption);
        command.Add(Options.PartOfOption);
        command.Add(Options.NoFollowOption);
        command.Add(Options.NoBuildOption);

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

                                return await handler.ExecuteAsync(loginContext, project, name, partOf, expose, follow: !noFollow, startBuild: !noBuild, cancellationToken);
                            })
                            .Build();

        return command;
    }

    private Command CreateDeleteCommand()
    {
        var command = CreateCommand("delete", "Delete an application");

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

                                return await handler.ExecuteAsync(loginContext, app, cancellationToken);
                            })
                            .Build();

        return command;
    }

    private Command CreateListCommand()
    {
        var command = CreateCommand("list", "List the deployments");

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

        LoginContext? loginContext = services.KubeConfig.GetCurrentContext();

        if (loginContext is null)
        {
            Console.WriteErrorLine("There is no connection context.");
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
        System.Exception? printException = null;
        try
        {
            return await next(ctx, cancellatinToken);
        }
        catch (System.OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteErrorLine("The command was aborted by the user.");
            return CommandResult.Failure;
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
        catch (System.Exception ex)
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