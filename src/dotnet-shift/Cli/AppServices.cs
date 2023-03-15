using Microsoft.Extensions.Logging.Abstractions;

record AppServices
{
    public string WorkingDirectory { get; }
    public ILogger Logger { get; }
    public IAnsiConsole Console { get; }
    public ILoginContextRepository KubeConfig { get; }
    public IOpenShiftClientFactory OpenShiftClientFactory { get; }
    public IProjectReader ProjectReader { get; }

    public AppServices(
        IAnsiConsole? console = null,
        ILogger? logger = null,
        string? workingDirectory = null,
        ILoginContextRepository? kubeConfig = null,
        IOpenShiftClientFactory? openshiftClientFactory = null,
        IProjectReader? projectReader = null)
    {
        Console ??= AnsiConsole.Console;
        Logger ??= NullLogger.Instance;
        WorkingDirectory ??= Directory.GetCurrentDirectory();
        KubeConfig ??= new Kubectl.KubernetesConfigFile();
        OpenShiftClientFactory ??= new OpenShift.OpenShiftClientFactory();
        ProjectReader ??= new MSBuild.ProjectReader();
    }
}