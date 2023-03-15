namespace CommandHandlers;

sealed partial class DeleteHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private IOpenShiftClientFactory OpenShiftClientFactory { get; }

    public DeleteHandler(IAnsiConsole console, ILogger logger, IOpenShiftClientFactory clientFactory)
    {
        Console = console;
        Logger = logger;
        OpenShiftClientFactory = clientFactory;
    }

    public async Task<int> ExecuteAsync(LoginContext login, string app, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();

        return CommandResult.Success;
    }
}