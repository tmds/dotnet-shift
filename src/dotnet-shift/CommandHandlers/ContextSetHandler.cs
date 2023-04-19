namespace CommandHandlers;

sealed class ContextSetHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private ILoginContextRepository KubeConfig { get; }

    public ContextSetHandler(IAnsiConsole console, ILogger logger, ILoginContextRepository kubeConfig)
    {
        Console = console;
        Logger = logger;
        KubeConfig = kubeConfig;
    }

    public Task<int> ExecuteAsync(string contextName, CancellationToken cancellationToken)
    {
        List<LoginContext> contexts = KubeConfig.GetAllContexts();

        LoginContext? context = contexts.FirstOrDefault(c => c.Name == contextName);

        if (context is null)
        {
            Console.WriteErrorLine($"The specified context '{contextName}' is not found.");
            return Task.FromResult(CommandResult.Failure);
        }

        KubeConfig.SetCurrentContext(contextName);

        string? host = null;
        if (Uri.TryCreate(context.Server, UriKind.Absolute, out Uri? uri))
        {
            host = uri.Host;
        }

        Console.WriteLine($"Using namespace '{context.Namespace}' on server '{host}'.");

        return Task.FromResult(CommandResult.Success);
    }
}