using OpenShift;
using CommandHandlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Testing;
using System.Text;

[UsesVerify]
public class DeployHandlerTests : IDisposable
{
    const string ProjectFileName = "project.csproj";

    private Lazy<string> _emptyDotnetProjectDirectory = new(() => CreateDotnetProjectDirectory());

    private string EmptyDotnetProjectFile => Path.Combine(_emptyDotnetProjectDirectory.Value, ProjectFileName);

    public void Dispose()
    {
        if (_emptyDotnetProjectDirectory.IsValueCreated)
        {
            try
            {
                Directory.Delete(_emptyDotnetProjectDirectory.Value, recursive: true);
            }
            catch
            { }
        }
    }

    private static string CreateDotnetProjectDirectory()
    {
        string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "empty-project")).FullName;

        File.WriteAllText(Path.Combine(directory, ProjectFileName), "");

        return directory;
    }

    private Task<int> DeployAsync(
                        MockOpenShiftServer server,

                        string? projectArg = null,
                        string? nameArg = null,
                        string? partOfArg = null,
                        bool exposeArg = true,
                        bool followArg = false,
                        bool startBuildArg = false,
                        CancellationToken cancellationTokenArg = default,
                        string? workingDirectory = null,
                        ProjectInformation? projectInfo = null,
                        MockProcessRunner.RunProcess? runProcess = null,
                        GitRepoInfo? repoInfo = null,
                        IAnsiConsole? console = null)
    {
        // The command handler doesn't use these values. The instance gets passed to create the OpenShiftClient.
        LoginContext login =
            new()
            {
                Name = "mycontext",
                Server = "https://myserver.com",
                Token = "mysecret",
                Username = "john_doe",
                Namespace = "mynamespace",
                SkipTlsVerify = false
            };

        // The command handler requres the project file to exist.
        projectArg ??= EmptyDotnetProjectFile;

        workingDirectory ??= Path.GetPathRoot(Path.GetTempPath())!;

        projectInfo ??=
            new()
            {
                DotnetVersion = "6.0",
                AssemblyName = "web"
            };

        MockOpenShiftClientFactory clientFactory = new(server, login.Namespace);
        MockProjectReader projectReader = new(projectInfo);
        MockGitRepoReader repoReader = new(repoInfo);
        if (runProcess is null)
        {
            runProcess = delegate { throw new NotSupportedException(); };
        }
        MockProcessRunner processRunner = new(runProcess);
        console ??= new TestConsole();
        ILogger logger = NullLogger.Instance;

        var handler = new DeployHandler(
            console: console,
            logger: NullLogger.Instance,
            workingDirectory: workingDirectory,
            clientFactory,
            projectReader,
            repoReader,
            processRunner
           );

        return handler.ExecuteAsync(login, projectArg, nameArg, partOfArg, exposeArg, followArg, startBuildArg, cancellationTokenArg);
    }

    [Fact]
    public async Task SuccessfulDeploymentWithFollow()
    {
        const string MyNamespace = "mynamespace";
        const string MyComponentName = "mycomponent";
        const string ImageSha = "sha256:deadbeef";
        const string ImageRegistry = $"image-registry.openshift-image-registry.svc:5000/{MyNamespace}/{MyComponentName}";

        using MockOpenShiftServer server = new();
        AddDotnetImageStreamAvailableController(server);
        AddBuildCompletedCompletedController(server, $"{MyComponentName}-binary-0", ImageRegistry, ImageSha);
        AddImageDeploymentCompletedController(server, MyComponentName, $"{ImageRegistry}@{ImageSha}");
        AddSetRouteHostController(server, MyComponentName, "myroutehost");
        AddBuilderRegistrySecret(server);
        AddPodController(server);

        Recorder recorder = new(new TestConsole());
        int rv = await DeployAsync(server,
            nameArg: MyComponentName,
            partOfArg: "myapp",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "5.0",
                AssemblyName = "myassembly"
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch1",
                RemoteUrl = "http://myurl"
            },
            startBuildArg: true, followArg: true,
            console: recorder,
            runProcess: CreateProcessRunForImageSha(ImageSha)
            );

        await Verify(recorder.ExportText());

        Assert.Equal(0, rv);
    }

    [Fact]
    public async Task ResourcesOnCreate()
    {
        const string MyNamespace = "mynamespace";
        const string MyComponentName = "mycomponent";
        const string ImageSha = "sha256:deadbeef";
        const string ImageRegistry = $"image-registry.openshift-image-registry.svc:5000/{MyNamespace}/{MyComponentName}";
        using MockOpenShiftServer server = new();
        AddDotnetImageStreamAvailableController(server);
        AddBuildCompletedCompletedController(server, $"{MyComponentName}-binary-0", ImageRegistry, ImageSha);
        AddBuilderRegistrySecret(server);
        AddPodController(server);

        int rv = await DeployAsync(server,
            nameArg: MyComponentName,
            partOfArg: "myapp",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "5.0",
                AssemblyName = "myassembly"
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch1",
                RemoteUrl = "http://myurl"
            },
            startBuildArg: true, followArg: false,
            runProcess: CreateProcessRunForImageSha(ImageSha)
            );

        Assert.Equal(0, rv);

        await Verify(server.ResourceSpecs);
    }

    private static void AddDotnetImageStreamAvailableController(MockOpenShiftServer server)
    {
        server.AddController(
            "dotnet",
            (ImageStream stream) =>
            {
                // Create a status tag with item so the handler assumes the dotnet version image is available.
                stream.Status ??= new();
                stream.Status.Tags ??= new();
                foreach (var tag in stream.Spec.Tags)
                {
                    stream.Status.Tags.Add(new()
                    {
                        Tag = tag.Name,
                        Items = new() { new() { } }
                    });
                }
            });
        server.AddController(
            "dotnet-runtime",
            (ImageStream stream) =>
            {
                // Create a status tag with item so the handler assumes the dotnet version image is available.
                stream.Status ??= new();
                stream.Status.Tags ??= new();
                foreach (var tag in stream.Spec.Tags)
                {
                    stream.Status.Tags.Add(new()
                    {
                        Tag = tag.Name,
                        Items = new() { new() { } }
                    });
                }
            });
    }

    private static void AddBuilderRegistrySecret(MockOpenShiftServer server)
    {
        server.Create<Secret>("builder-dockercfg-test", new Secret()
        {
            Type = "type=kubernetes.io/dockercfg",
            Metadata = new()
            {
                Name = "builder-dockercfg-test",
                Annotations = new Dictionary<string, string>()
                {
                    { "kubernetes.io/service-account.name", "builder" }
                }
            },
            Data = new Dictionary<string, byte[]>()
            {
                { ".dockercfg", Encoding.UTF8.GetBytes(
                    """
                    {
                        "image-registry.openshift-image-registry.svc:5000": {
                            "username": "serviceaccount",
                            "password": "secret123",
                            "email": "serviceaccount@example.org"
                        }
                    }
                    """
                ) }
            }
        });
    }

    private static void AddPodController(MockOpenShiftServer server)
    {
        server.AddController(
            (Pod pod) =>
            {
                // Create a status tag with item so the handler assumes the dotnet version image is available.
                pod.Status ??= new();
                pod.Status.Phase = PodStatusPhase.Running;
            });
        server.ExecutePodCommand = (Pod pod, IEnumerable<string> command, CancellationToken cancellationToken) =>
        {
            return new MockProcess();
        };
    }

    private static void AddImageDeploymentCompletedController(MockOpenShiftServer server, string deployment, string deployedImage)
    {
        server.AddController(
            deployment,
            (Deployment deployment) =>
            {
                deployment.Spec.Template.Spec.Containers[0].Image = deployedImage;
                deployment.Metadata.Generation = 1;
                deployment.Status ??= new();
                deployment.Status.ObservedGeneration = deployment.Metadata.Generation;
                deployment.Status.Conditions ??= new();
                deployment.Status.Conditions.Add(new()
                {
                    Type = "Progressing",
                    Status = "True",
                    Reason = "NewReplicaSetAvailable"
                });
                deployment.Status.AvailableReplicas = 1;
            });
    }

    private static void AddBuildCompletedCompletedController(MockOpenShiftServer server, string build, string imageRegistry, string imageSha)
    {
        server.AddController(
            build,
            (Build build) =>
            {
                // The build completed.
                build.Status.Phase = "Complete";
                build.Status.OutputDockerImageReference = $"{imageRegistry}:latest";
                build.Status.Output ??= new();
                build.Status.Output.To ??= new();
                build.Status.Output.To.ImageDigest = imageSha;
            });
    }

    private static void AddSetRouteHostController(MockOpenShiftServer server, string route, string host)
    {
        server.AddController(
            route,
            (Route route) =>
            {
                route.Spec.Host = host;
            });
    }

    private static MockProcessRunner.RunProcess CreateProcessRunForImageSha(string imageSha)
    {
        return (string filename, IEnumerable<string> args, IDictionary<string, string?>? envvars) =>
        {
            string stdout;
            if (filename == "dotnet" && args.Contains("/p:PublishProfile=DefaultContainer") && args.Contains("--getProperty:GeneratedContainerDigest"))
            {
                stdout = imageSha;
            }
            else
            {
                stdout = $"Running {filename} {string.Join(" ", args)}.";
            }
            return new MockProcess(stdout: stdout);
        };
    }

    [Fact]
    public async Task ResourcesOnUpdate()
    {
        const string MyNamespace = "mynamespace";
        const string MyComponentName = "mycomponent";
        const string ImageSha = "sha256:deadbeef";
        const string ImageRegistry = $"image-registry.openshift-image-registry.svc:5000/{MyNamespace}/{MyComponentName}";
        using MockOpenShiftServer server = new();
        AddDotnetImageStreamAvailableController(server);
        AddBuildCompletedCompletedController(server, $"{MyComponentName}-binary-0", ImageRegistry, ImageSha);
        AddBuilderRegistrySecret(server);
        AddPodController(server);

        int rv = await DeployAsync(server,
            nameArg: MyComponentName,
            partOfArg: "myapp_1",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "3.0",
                AssemblyName = "myassembly_1"
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch_1",
                RemoteUrl = "http://myurl_1"
            },
            startBuildArg: true,
            runProcess: CreateProcessRunForImageSha(ImageSha));
        Assert.Equal(0, rv);

        rv = await DeployAsync(server,
            nameArg: MyComponentName,
            partOfArg: "myapp_2",
            exposeArg: true,
            projectInfo: new()
            {
                DotnetVersion = "4.0",
                AssemblyName = "myassembly_2"
            },
            repoInfo: new()
            {
                RemoteBranch = "mybranch_2",
                RemoteUrl = "http://myurl_2"
            },
            startBuildArg: false);
        Assert.Equal(0, rv);

        await Verify(server.ResourceSpecs);
    }
}