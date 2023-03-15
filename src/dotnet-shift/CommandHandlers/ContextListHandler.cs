namespace CommandHandlers;

sealed class ContextListHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private ILoginContextRepository KubeConfig { get; }

    public ContextListHandler(IAnsiConsole console, ILogger logger, ILoginContextRepository kubeConfig)
    {
        Console = console;
        Logger = logger;
        KubeConfig = kubeConfig;
    }

    public Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        List<LoginContext> contexts = KubeConfig.GetAllContexts(includeTokens: false);

        if (contexts.Count == 0)
        {
            Console.WriteLine("There are no contexts.");
            Console.WriteLine();
            Console.WriteLine("You can add one using the 'login' command.");
            return Task.FromResult(CommandResult.Success);
        }

        var grid = new Grid();

        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(new[]{
            "NAME",
            "SERVER",
            "NAMESPACE",
            "USERNAME",
        });

        foreach (var context in contexts)
        {
            // Add content row 
            grid.AddRow(new[]{
                context.Name,
                context.Server,
                context.Namespace,
                context.Username
            });
        }

        AnsiConsole.Write(grid);

        return Task.FromResult(CommandResult.Success);
    }
}