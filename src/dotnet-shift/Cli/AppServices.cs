using Microsoft.Extensions.Logging.Abstractions;

sealed class AppServices
{
    public string WorkingDirectory { get; }
    public ILogger Logger { get; }
    public IAnsiConsole Console { get; }
    public ILoginContextRepository KubeConfig { get; }
    public ILoginContextProvider LoginProvider { get; }
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
        consoleSettings ??= new AnsiConsoleSettings();
        bool isOutputRedirected = System.Console.IsOutputRedirected;
        if (isOutputRedirected)
        {
            // Don't use ANSI escape sequences when output is redirected.
            consoleSettings.Ansi = AnsiSupport.No;
        }
        Console = AnsiConsole.Create(consoleSettings);
        if (isOutputRedirected)
        {
            // Don't wrap at 80 columns when output is redirected.
            Console.Profile.Width = int.MaxValue / 2;
        }

        Logger = logger ?? NullLogger.Instance;
        WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        KubeConfig = kubeConfig ?? new Kubectl.KubernetesConfigFile();
        LoginProvider = new Kubectl.LoginContextProvider(KubeConfig);
        OpenShiftClientFactory = openshiftClientFactory ?? new OpenShift.OpenShiftClientFactory();
        ProjectReader = projectReader ?? new MSBuild.ProjectReader();
        GitRepoReader = repoReader ?? new Git.GitRepoReader();
    }
}