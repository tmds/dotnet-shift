using System.CommandLine;
using CommandHandlers;

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

            var handler = new LoginCommandHandler(services.Console, services.Logger, services.OpenShiftClientFactory, services.KubeConfig);

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

        command.Handler = CreateHandlerBuilder()
                            .Filter(GetLoginContext)
                            .Handle(async (AppContext ctx, CancellationToken cancellationToken) =>
                            {
                                var services = ctx.Services;
                                var parseResult = ctx.ParseResult;

                                var handler = new DeployHandler(services.Console, services.Logger, services.WorkingDirectory, services.OpenShiftClientFactory, services.ProjectReader);

                                LoginContext loginContext = ctx.LoginContext!;

                                string project = parseResult.GetValue(Options.RequiredDeployProjectArgument)!;
                                bool expose = parseResult.GetValue(Options.ExposeOption);
                                string? partOf = parseResult.GetValue(Options.PartOfOption);
                                string? name = parseResult.GetValue(Options.DeploymentNameOption);

                                return await handler.ExecuteAsync(loginContext, project, name, partOf, expose, cancellationToken);
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

    static async Task<int> GetLoginContext(AppContext ctx, Handler next, CancellationToken cancellatinToken)
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

            return await next(ctx, cancellatinToken);
        }
    }
}