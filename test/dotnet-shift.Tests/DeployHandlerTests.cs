using CommandHandlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

[UsesVerify]
public class DeployHandlerTests : IDisposable
{
    private Lazy<string> _emptyFile = new(() => Path.GetTempFileName());

    private string EmptyFile => _emptyFile.Value;

    public void Dispose()
    {
        if (_emptyFile.IsValueCreated)
        {
            try
            {
                File.Delete(EmptyFile);
            }
            catch
            { }
        }
    }

    private Task<int> DeployAsync(
                        MockOpenShiftClient client,

                        string? projectArg = null,
                        string? nameArg = null,
                        string? partOfArg = null,
                        bool exposeArg = true,
                        bool followArg = false,
                        bool startBuildArg = false,
                        CancellationToken cancellationTokenArg = default,

                        string? workingDirectory = null,
                        ProjectInformation? projectInfo = null,
                        GitRepoInfo? repoInfo = null)
    {
        // The command handler doesn't use these values. The instance gets passed to create the OpenShiftClient.
        LoginContext login =
            new()
            {
                Name = "Production",
                Server = "fake.production.com",
                Token = "<secret>",
                Username = "john_doe",
                Namespace = "default",
                SkipTlsVerify = false
            };

        // The command handler requres the project file to exist.
        projectArg ??= EmptyFile;

        workingDirectory ??= Path.GetPathRoot(Path.GetTempPath())!;

        projectInfo ??=
            new()
            {
                DotnetVersion = "6.0",
                AssemblyName = "web",
                ContainerEnvironmentVariables = new()
            };

        MockOpenShiftClientFactory clientFactory = new(client);
        MockProjectReader projectReader = new(projectInfo);
        MockGitRepoReader repoReader = new(repoInfo);
        MockConsole console = new();
        ILogger logger = NullLogger.Instance;

        var handler = new DeployHandler(
            console: console,
            logger: NullLogger.Instance,
            workingDirectory: workingDirectory,
            clientFactory,
            projectReader,
            repoReader
           );

        return handler.ExecuteAsync(login, projectArg, nameArg, partOfArg, exposeArg, followArg, startBuildArg, cancellationTokenArg);
    }

    [Fact]
    public async Task ResourcesOnCreate()
    {
        MockOpenShiftClient client = new();

        int rv = await DeployAsync(client,
            nameArg: "mycomponent",
            partOfArg: "myapp",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "5.0",
                AssemblyName = "myassembly",
                ContainerEnvironmentVariables = new()
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch1",
                RemoteUrl = "http://myurl"
            });

        Assert.Equal(0, rv);

        await Verify(client.ServerResources);
    }

    [Fact]
    public async Task ResourcesOnUpdate()
    {
        MockOpenShiftClient client = new();

        // We create, and update the same component.
        const string ComponentName = "mycomponent";

        int rv = await DeployAsync(client,
            nameArg: ComponentName,
            partOfArg: "myapp_1",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "3.0",
                AssemblyName = "myassembly_1",
                ContainerEnvironmentVariables = new()
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch_1",
                RemoteUrl = "http://myurl_1"
            });
        Assert.Equal(0, rv);

        rv = await DeployAsync(client,
            nameArg: ComponentName,
            partOfArg: "myapp_2",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "4.0",
                AssemblyName = "myassembly_2",
                ContainerEnvironmentVariables = new()
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch_2",
                RemoteUrl = "http://myurl_2"
            });
        Assert.Equal(0, rv);

        await Verify(client.ServerResources);
    }
}