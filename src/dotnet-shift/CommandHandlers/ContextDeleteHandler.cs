namespace CommandHandlers;

sealed class ContextDeleteHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private ILoginContextRepository KubeConfig { get; }

    public ContextDeleteHandler(IAnsiConsole console, ILogger logger, ILoginContextRepository kubeConfig)
    {
        Console = console;
        Logger = logger;
        KubeConfig = kubeConfig;
    }

    public Task<int> ExecuteAsync(string contextName, CancellationToken cancellationToken)
    {
        bool deleted = KubeConfig.DeleteContext(contextName);

        if (deleted)
        {
            Console.WriteLine($"Removed context '{contextName}'.");
        }
        else
        {
            Console.WriteErrorLine($"Context '{contextName}' was not found.");
        }

        return Task.FromResult(CommandResult.Success);
    }
}