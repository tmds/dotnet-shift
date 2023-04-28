using Microsoft.Extensions.Logging.Abstractions;

sealed class AppServices
{
    public string WorkingDirectory { get; }
    public ILogger Logger { get; }
    public IAnsiConsole Console { get; }
    public ILoginContextRepository KubeConfig { get; }
    public IOpenShiftClientFactory OpenShiftClientFactory { get; }
    public IProjectReader ProjectReader { get; }
    public IGitRepoReader GitRepoReader { get; }

    public AppServices(
        AnsiConsoleSettings? consoleSettings = null,
        ILogger? logger = null,
        string? workingDirectory = null,
        ILoginContextRepository? kubeConfig = null,
        IOpenShiftClientFactory? openshiftClientFactory = null,
        IProjectReader? projectReader = null,
        IGitRepoReader? repoReader = null)
    {
        Console = consoleSettings is not null ? AnsiConsole.Create(consoleSettings) : AnsiConsole.Console;
        Logger = logger ?? NullLogger.Instance;
        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        KubeConfig = kubeConfig ?? new Kubectl.KubernetesConfigFile();
        OpenShiftClientFactory = openshiftClientFactory ?? new OpenShift.OpenShiftClientFactory();
        ProjectReader = projectReader ?? new MSBuild.ProjectReader();
        GitRepoReader = repoReader ?? new Git.GitRepoReader();
    }
}