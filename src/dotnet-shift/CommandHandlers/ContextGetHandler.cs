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

    public Task<int> ExecuteAsync(LoginContext context, CancellationToken cancellationToken)
    {
        var grid = new Grid();

        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(new[] { "Name:", context.Name });
        grid.AddRow(new[] { "Server:", context.Server });
        grid.AddRow(new[] { "Namespace:", context.Namespace });
        grid.AddRow(new[] { "Username:", context.Username });

        Console.Write(grid);

        return Task.FromResult(CommandResult.Success);
    }
}