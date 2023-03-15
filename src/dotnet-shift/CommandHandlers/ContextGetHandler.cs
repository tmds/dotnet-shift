namespace CommandHandlers;

sealed class ContextGetHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private ILoginContextRepository KubeConfig { get; }

    public ContextGetHandler(IAnsiConsole console, ILogger logger, ILoginContextRepository kubeConfig)
    {
        Console = console;
        Logger = logger;
        KubeConfig = kubeConfig;
    }

    public Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        LoginContext? context = KubeConfig.GetCurrentContext();

        if (context is null)
        {
            Console.WriteLine("There is no connection context.");
            Console.WriteLine();
            Console.WriteLine("You can create a new connection using the 'login' command.");
            Console.WriteLine("You can list the available contexts using the 'context list' command, and select one using the 'context set' command.");
            return Task.FromResult(CommandResult.Failure);
        }

        var grid = new Grid();

        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(new[] { "Name:", context.Name });
        grid.AddRow(new[] { "Server:", context.Server });
        grid.AddRow(new[] { "Namespace:", context.Namespace });
        grid.AddRow(new[] { "Username:", context.Username });

        AnsiConsole.Write(grid);

        return Task.FromResult(CommandResult.Success);
    }
}