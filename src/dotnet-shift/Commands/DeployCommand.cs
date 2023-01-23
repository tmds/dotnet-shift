using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LibGit2Sharp;

sealed partial class DeployCommand : Command
{
    private const string Runtime = "dotnet";
    private const string ContainerName = "app";
    private const string DotnetImageStreamName = "dotnet";

    record ResourceProperties
    {
        public required string AppName { get; init; }
        public required string Instance { get; init; }

        public required string DotnetAppName { get; init; }
        public required string DotnetS2iImage { get; init; }
        public required string DotnetVersion { get; init; }
        public required string DotnetComponent { get; init; }
        public required string? DotnetSourceSecret { get; init; }

        public string? DotnetAppGitUri { get; init; }
        public string? DotnetAppGitRef { get; init; }

        public string DotnetAppImageName => DotnetAppName;
        public string DotnetBinaryBuildConfigName => $"{DotnetAppName}-binary";
    }

    public static readonly Argument<string> ProjectArgument =
        new Argument<string>("PROJECT", defaultValueFactory: () => ".", ".NET project to build");

    public static readonly Option<string> AsFileOption =
        new Option<string>("--as-file", "Generates a JSON file with resources")
    {
        ArgumentHelpName = "FILE"
    };

    public static readonly Option<string> NameOption =
        new Option<string>("--name", "Name the deployment")
    {
        ArgumentHelpName = "DEPLOYMENT"
    };

    public static readonly Option<string> PartOfOption =
        new Option<string>("--part-of", "Add to application")
    {
        ArgumentHelpName = "APP"
    };

    public static readonly Option<string> ContextOption =
        new Option<string>(new[] { "--context" }, "Context directory for the image build")
    {
        ArgumentHelpName = "DIR"
    };

    public static readonly Option<bool> FromGitOption =
        new Option<bool>(new[] { "--from-git" }, "Build using the git repository")
    {
        Arity = ArgumentArity.Zero
    };

    public static readonly Option<string> SourceSecretOption =
        new Option<string>(new[] { "--source-secret" }, "Secret used for cloning the source code")
    {
        ArgumentHelpName = "SECRET"
    };

    public DeployCommand() : base("deploy", "Deploys .NET application")
    {
        Add(ProjectArgument);

        Add(ContextOption);
        Add(NameOption);
        Add(PartOfOption);

        Add(FromGitOption);

        Add(SourceSecretOption);

        Add(AsFileOption);

        this.SetHandler((project, asFile, context, fromGit, sourceSecret, name, partOf) => HandleAsync(project, asFile, context, fromGit, sourceSecret, name, partOf), ProjectArgument, AsFileOption, ContextOption, FromGitOption, SourceSecretOption, NameOption, PartOfOption);
    }

    public static async Task<int> HandleAsync(string project, string? asFile, string? context, bool fromGit, string? sourceSecret, string? name, string? partOf)
    {
        if (!fromGit)
        {
            if (sourceSecret is not null)
            {
                Console.Error.WriteLine($"The --source-secret option is only used when --from-git is specified and will be ignored.");
                sourceSecret = null;
            }
        }

        // Find the .NET project file.
        string projectFullPath = Path.Combine(Directory.GetCurrentDirectory(), project);
        string? projectFile = null;
        if (File.Exists(projectFullPath))
        {
            projectFile = Path.GetFullPath(projectFullPath);
        }
        else if (Directory.Exists(projectFullPath))
        {
            var projFiles = Directory.GetFiles(projectFullPath, "*.??proj");
            if (projFiles.Length > 0)
            {
                projectFile = Path.GetFullPath(projFiles[0]);
            }
        }
        if (projectFile is null)
        {
            Console.Error.WriteLine($"Project {projectFullPath} not found.");
            return 1;
        }

        // Determine context directory.
        string? contextDir = null;
        if (context is not null)
        {
            contextDir = Path.GetFullPath(context);

            if (fromGit)
            {
                if (!Directory.Exists(Path.Combine(contextDir, ".git")))
                {
                    Console.Error.WriteLine($"The specified context directory must be a git repository when using --from-git.");
                    return 1;
                }
            }
        }
        else
        {
            // Guess what would be an appropriate context directory.
            // Move up directory by directory until we reach a directory that has a *.sln file, or a .git subdirectory.
            string projectFileDirectory = Path.GetDirectoryName(projectFile)!;

            // Move up directory by directory util we we have a .git subdirectory.
            contextDir = FindParentDir(projectFileDirectory, parentDir => Directory.Exists(Path.Combine(parentDir, ".git")));
            if (fromGit && contextDir is null)
            {
                Console.Error.WriteLine($"The specified project must be part of a git repository when using --from-git.");
                return 1;
            }

            if (contextDir is null)
            {
                // Move up directory by directory util we we have a *.sln file.
                contextDir = FindParentDir(projectFileDirectory, parentDir => Directory.GetFiles(parentDir, "*.sln").Length > 0);
            }

            // Avoid using '/tmp' and '~' as guesses.
            if (contextDir == Path.GetTempPath() ||
                contextDir == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            {
                contextDir = null;
            }

            // Default to the project file directory.
            if (contextDir is null)
            {
                contextDir = projectFileDirectory;
            }

            if (contextDir != projectFileDirectory)
            {
                Console.WriteLine($"Using context directory '{contextDir}'");
            }
        }

        // verify the project we're build is under the contextDir.
        // and determine DOTNET_STARTUP_PROJECT.
        if (!projectFile.StartsWith(contextDir))
        {
            Console.Error.WriteLine($"Project must be a subdirectory of the context directory.");
            return 1;
        }
        Dictionary<string, string> buildEnvironment = new();
        string dotnetStartupProject = projectFile.Substring(contextDir.Length).TrimStart(Path.DirectorySeparatorChar);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            dotnetStartupProject = dotnetStartupProject.Replace('\\', '/');
        }
        buildEnvironment["DOTNET_STARTUP_PROJECT"] = dotnetStartupProject;

        // Find out .NET version and assembly name.
        ProjectInformation projectInformation = ProjectReader.ReadProjectInfo(projectFile);
        if (projectInformation.DotnetVersion is null || projectInformation.AssemblyName is null)
        {
            if (projectInformation.DotnetVersion is null)
            {
                Console.Error.WriteLine($"Cannot determine project target framework version.");
            }
            if (projectInformation.AssemblyName is null)
            {
                Console.Error.WriteLine($"Cannot determine application assembly name.");
            }
            return 1;
        }

        (string? gitUri, string? gitRef) = DetermineGitRemote(contextDir);

        if (fromGit)
        {
            if (gitUri is null || gitRef is null)
            {
                Console.Error.WriteLine($"Cannot determine remote branch to build.");
                return 1;
            }

            Console.WriteLine($"Application will be built from {gitUri}#{gitRef}");
        }

        // Generate resource definitions.
        if (name is null)
        {
            name = projectInformation.AssemblyName;
            // a lowercase RFC 1123 subdomain must consist of lower case alphanumeric characters, '-' or '.', and must start and end with an alphanumeric character
            name = name.Replace(".", "-").ToLowerInvariant();
        }
        if (partOf is null)
        {
            partOf = name;
        }

        string s2iImage = GetS2iImage(projectInformation.DotnetVersion);
        var props = new ResourceProperties()
        {
            AppName = partOf,
            Instance = partOf,

            DotnetAppName = name,
            DotnetComponent = name,
            DotnetVersion = projectInformation.DotnetVersion,
            DotnetS2iImage = s2iImage,
            DotnetAppGitRef = gitRef,
            DotnetAppGitUri = gitUri,
            DotnetSourceSecret = sourceSecret
        };

        string dotnetImageStreamTag = GenerateDotnetImageStreamTag(props);
        string deploymentConfig = GenerateDeploymentConfig(props, includeGitAnnotations: true);
        string service = GenerateService(props);
        string imageStream = GenerateImageStream(props);
        // TODO: can we use the source-buildconfig for binary builds?
        string buildConfig = fromGit ? GenerateSourceBuildConfig(props, buildEnvironment) : GenerateBinaryBuildConfig(props);
        // TODO: add --expose option.
        string route = GenerateRoute(props);

        if (asFile is null)
        {
            // Update the resources.
            var client = new OpenShiftClient();

            if (sourceSecret is not null)
            {
                bool exists = await client.ExistsSecretAsync(sourceSecret);
                if (!exists)
                {
                    Console.Error.WriteLine($"The specified source secret '{sourceSecret}' does not exist.");
                    Console.Error.WriteLine($"You can create it using the 'secret' command.");
                    return 1;
                }
            }

            bool added = await client.CreateImageStreamTagAsync(DotnetImageStreamName, dotnetImageStreamTag);
            if (added)
            {
                Console.WriteLine($"Added '{DotnetImageStreamName}:{props.DotnetVersion}' image stream tag");
            }

            Console.WriteLine("Update DeploymentConfig");
            await client.ApplyDeploymentConfigAsync(deploymentConfig);

            Console.WriteLine("Update Service");
            await client.ApplyServiceAsync(service);

            Console.WriteLine("Update ImageStream");
            await client.ApplyImageStreamAsync(imageStream);

            Console.WriteLine("Update BuildConfig");
            await client.ApplyBuildConfigAsync(buildConfig);

            Console.WriteLine("Update Route");
            await client.ApplyRouteAsync(route);

            Console.WriteLine("Start build");
            if (fromGit)
            {
                await client.StartBuildAsync(props.DotnetAppName);
            }
            else
            {
                // build from local sources.
                using Stream archiveStream = CreateApplicationArchive(contextDir, buildEnvironment);
                await client.StartBinaryBuildAsync(props.DotnetBinaryBuildConfigName, archiveStream);
            }

            // ** Any resource types added here must be added to the 'delete' command too. ** //

            // TODO: follow build.
            // TODO: print url.
        }
        else
        {
            // Write the resources to a file.
            StringBuilder content = new();
            content.Append("""
                           {
                               "apiVersion": "v1",
                               "kind": "List",
                               "items": [
                           """);
            content.Append(deploymentConfig);

            content.Append(",");
            content.Append(service);

            content.Append(",");
            content.Append(imageStream);

            content.Append(",");
            content.Append(buildConfig);

            content.Append(",");
            content.Append(route);

            content.Append("""
                               ]
                           }
                           """);
            string json = JsonTryToPrettify(content.ToString());
            File.WriteAllText(asFile, json);
        }
        return 0;
    }

    private static string? FindParentDir(string projectFileDirectory, Func<string, bool> predicate)
    {
        string parentDir = projectFileDirectory;
        do
        {
            if (predicate(parentDir))
            {
                return parentDir;
            }
            parentDir = Path.GetDirectoryName(parentDir)!;
            if (parentDir == Path.GetPathRoot(parentDir))
            {
                return null;
            }
        } while (true);
    }

    private static (string? gitUri, string? gitRef) DetermineGitRemote(string root)
    {
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            return (null, null);
        }

        var gitRepo = new Repository(root);

        Branch? remoteBranch = gitRepo.Head?.TrackedBranch;
        if (remoteBranch is null)
        {
            return (null, null);
        }

        // determine gitUri
        string remoteName = remoteBranch.RemoteName;
        Remote remote = gitRepo.Network.Remotes.First(r => r.Name == remoteName);
        string gitUri = remote.Url;

        // determine gitRef
        string canonicalBranchName = remoteBranch.UpstreamBranchCanonicalName;
        if (!canonicalBranchName.StartsWith("refs/heads/"))
        {
            return (null, null);
        }
        string gitRef = canonicalBranchName.Substring("refs/heads/".Length);

        return (gitUri, gitRef);
    }

    private static string JsonTryToPrettify(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true
                }
            );
            MemoryStream memoryStream = new MemoryStream();
            using (
                var utf8JsonWriter = new Utf8JsonWriter(
                    memoryStream,
                    new JsonWriterOptions
                    {
                        Indented = true
                    }
                )
            )
            {
                doc.WriteTo(utf8JsonWriter);
            }
            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }
        catch
        {
            // JSON invalid?
            return json;
        }
    }

    private static string GetS2iImage(string version)
    {
        string versionNoDot = version.Replace(".", "");

        return $"registry.access.redhat.com/{DotNetVersionToRedHatBaseImage(version)}/dotnet-{versionNoDot}:latest";

        static string DotNetVersionToRedHatBaseImage(string version) => version switch
        {
            _ => "ubi8"
        };
    }

    private static string GenerateDotnetImageStreamTag(ResourceProperties properties)
    {
        // note: we're setting a scheduled importPolicy so OpenShift will check
        //       the image registry for updates.
        return $$"""
        {
            "name": "{{properties.DotnetVersion}}",
            "annotations": {
                "openshift.io/display-name": ".NET {{properties.DotnetVersion}}"
            },
            "referencePolicy": {
                "type": "Local"
            },
            "from": {
                "kind": "DockerImage",
                "name": "{{properties.DotnetS2iImage}}"
            },
            "importPolicy": {
                "scheduled": "true"
            }
        }
        """;
    }

    private static string GenerateDeploymentConfig(ResourceProperties properties, bool includeGitAnnotations)
    {
        return $$"""
        {
            "apiVersion": "apps.openshift.io/v1",
            "kind": "DeploymentConfig",
            "metadata": {
                "name": "{{properties.DotnetAppName}}",
                "annotations": {
                    {{DotnetAnnotations(properties, includeGitAnnotations)}}
                },
                "labels": {
                    {{DotnetResourceLabels(properties, includeRuntimeLabels: true)}}
                }
            },
            "spec": {
                "replicas": 1,
                "selector": {
                    {{DotnetContainerLabels(properties, isSelector: true)}}
                },
                "triggers": [
                    {
                        "type": "ConfigChange"
                    },
                    {
                        "type": "ImageChange",
                        "imageChangeParams": {
                            "automatic": true,
                            "containerNames": [
                                "{{ContainerName}}"
                            ],
                            "from": {
                                "kind": "ImageStreamTag",
                                "name": "{{properties.DotnetAppImageName}}:latest"
                            }
                        }
                    }
                ],
                "template": {
                    "metadata": {
                        "labels": {
                            {{DotnetContainerLabels(properties, isSelector: false)}}
                        }
                    },
                    "spec": {
                        "containers": [
                            {
                                "name": "{{ContainerName}}",
                                "image": "{{properties.DotnetAppImageName}}",
                                "securityContext": {
                                    "privileged": false
                                },
                                "ports": [
                                {
                                    "containerPort": 8080,
                                    "name": "http",
                                    "protocol": "TCP"
                                }
                                ],
                                "env": []
                            }
                        ]
                    }
                }
            }
        }
        """;
    }

    private static string GenerateService(ResourceProperties properties)
    {
        return $$"""
        {
            "apiVersion": "v1",
            "kind": "Service",
            "spec": {
                "type": "ClusterIP",
                "selector": {
                    {{DotnetContainerLabels(properties, isSelector: true)}}
                },
                "ports": [
                {
                    "protocol": "TCP",
                    "port": 8080,
                    "name": "http"
                }
                ]
            },
            "metadata": {
                "name": "{{properties.DotnetAppName}}",
                "labels": {
                    {{DotnetResourceLabels(properties, includeRuntimeLabels: false)}}
                }
            }
        }
        """;
    }

    private static string GenerateImageStream(ResourceProperties properties)
    {
        return $$"""
        {
            "apiVersion": "image.openshift.io/v1",
            "kind": "ImageStream",
            "metadata": {
                "labels": {
                    {{DotnetResourceLabels(properties, includeRuntimeLabels: false)}}
                },
                "name": "{{properties.DotnetAppName}}"
            }
        }
        """;
    }

    private static string GenerateSourceBuildConfig(ResourceProperties properties, Dictionary<string, string> environment)
    {
        // note: ImageChange trigger causes the app image to be rebuilt when a new s2i base image is available.

        // TODO: support adding webhooks
        return $$"""
        {
            "apiVersion": "build.openshift.io/v1",
            "kind": "BuildConfig",
            "metadata": {
                "labels": {
                    {{DotnetResourceLabels(properties, includeRuntimeLabels: true)}}
                },
                "name": "{{properties.DotnetAppName}}"
            },
            "spec": {
                "failedBuildsHistoryLimit": 5,
                "successfulBuildsHistoryLimit": 5,
                "output": {
                    "to": {
                        "kind": "ImageStreamTag",
                        "name": "{{properties.DotnetAppImageName}}:latest"
                    }
                },
                "strategy": {
                    "sourceStrategy": {
                        "from": {
                            "kind": "ImageStreamTag",
                            "name": "{{DotnetImageStreamName}}:{{properties.DotnetVersion}}"
                        },
                        "env": [
                            {{GenerateEnvSection(environment)}}
                        ]
                    },
                    "type": "Source"
                },
                "source": {
                    "type": "Git",
                    "git": {
                        "uri": "{{properties.DotnetAppGitUri!}}",
                        "ref": "{{properties.DotnetAppGitRef!}}"
                    }
                    {{GenerateSourceSecretSection(properties.DotnetSourceSecret)}}
                },
                "triggers": [
                    {
                        "type": "ImageChange",
                        "imageChange": {}
                    }
                ],
                "runPolicy": "Serial"
            }
        }
        """;

        static string GenerateEnvSection(Dictionary<string, string> environment)
        {
            StringBuilder sb = new();
            bool first = true;
            foreach (var envvar in environment)
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                else
                {
                    sb.Append("  ");
                }
                first = false;

                sb.Append($$"""
                        {
                            "name": "{{envvar.Key}}",
                            "value": "{{envvar.Value}}"
                        }
                        """);
            }
            return sb.ToString();
        }

        static string GenerateSourceSecretSection(string? sourceSecret)
        {
            if (sourceSecret is null)
            {
                return "";
            }
            else
            {
                return $$"""
                            ,
                            "sourceSecret": {
                                "name": "{{sourceSecret}}"
                            }
                        """;
            }
        }
    }

    private static string GenerateBinaryBuildConfig(ResourceProperties properties)
    {
        return $$"""
        {
            "apiVersion": "build.openshift.io/v1",
            "kind": "BuildConfig",
            "metadata": {
                "labels": {

                    {{DotnetResourceLabels(properties, includeRuntimeLabels: true)}}
                },
                "name": "{{properties.DotnetBinaryBuildConfigName}}"
            },
            "spec": {
                "failedBuildsHistoryLimit": 5,
                "successfulBuildsHistoryLimit": 5,
                "output": {
                    "to": {
                        "kind": "ImageStreamTag",
                        "name": "{{properties.DotnetAppImageName}}:latest"
                    }
                },
                "source": {
                    "type": "Binary"
                },
                "strategy": {
                    "sourceStrategy": {
                        "from": {
                            "kind": "ImageStreamTag",
                            "name": "{{DotnetImageStreamName}}:{{properties.DotnetVersion}}"
                        }
                    },
                    "type": "Source"
                }
            }
        }
        """;
    }

    private static string GenerateRoute(ResourceProperties properties)
    {
        return $$"""
        {
            "apiVersion": "route.openshift.io/v1",
            "kind": "Route",
            "spec": {
                "to": {
                    "kind": "Service",
                    "name": "{{properties.DotnetAppName}}"
                },
                "port": {
                    "targetPort": 8080
                }
            },
            "metadata": {
                "name": "{{properties.DotnetAppName}}",
                "labels": {
                    {{DotnetResourceLabels(properties, includeRuntimeLabels: false)}}
                }
            }
        }
        """;
    }

    private static string DotnetResourceLabels(ResourceProperties properties, bool includeRuntimeLabels)
    {
        string labels = $$"""
                      "app.kubernetes.io/managed-by": "{{LabelConstants.DotnetShift}}"
                    , "app.kubernetes.io/part-of": "{{properties.AppName}}"
                    , "app.kubernetes.io/name": "{{properties.DotnetAppName}}"
                    , "app.kubernetes.io/component": "{{properties.DotnetComponent}}"
                    , "app.kubernetes.io/instance": "{{properties.Instance}}"
        """;

        if (includeRuntimeLabels)
        {
            labels += $$"""
                    , "app.openshift.io/runtime": "{{LabelConstants.DotnetRuntime}}"
        """;
        }

        return labels;
    }

    private static string DotnetAnnotations(ResourceProperties properties, bool includeGitAnnotations)
    {
        string annotations = "";

        if (includeGitAnnotations && (properties.DotnetAppGitRef is not null && properties.DotnetAppGitUri is not null))
        {
            annotations += $$"""
                    "app.openshift.io/vcs-ref": "{{properties.DotnetAppGitRef}}",
                    "app.openshift.io/vcs-uri": "{{properties.DotnetAppGitUri}}"
        """;
        }

        return annotations;
    }

    private static string DotnetContainerLabels(ResourceProperties properties, bool isSelector)
    {
        string labels = $$"""
                    "app": "{{properties.DotnetAppName}}"
        """;
        if (!isSelector)
        {

        }
        return labels;
    }
}
