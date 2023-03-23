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
        IAnsiConsole? console = null,
        ILogger? logger = null,
        string? workingDirectory = null,
        ILoginContextRepository? kubeConfig = null,
        IOpenShiftClientFactory? openshiftClientFactory = null,
        IProjectReader? projectReader = null,
        IGitRepoReader? repoReader = null)
    {
        Console ??= AnsiConsole.Console;
        Logger ??= NullLogger.Instance;
        WorkingDirectory ??= Directory.GetCurrentDirectory();
        KubeConfig ??= new Kubectl.KubernetesConfigFile();
        OpenShiftClientFactory ??= new OpenShift.OpenShiftClientFactory();
        ProjectReader ??= new MSBuild.ProjectReader();
        GitRepoReader ??= new Git.GitRepoReader();
    }
}