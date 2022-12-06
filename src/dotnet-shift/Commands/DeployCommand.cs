using System.CommandLine;
using System.Formats.Tar;
using System.IO.Pipelines;
using System.Diagnostics.CodeAnalysis;

sealed class DeployCommand : Command
{
    private const string DotnetShift = "dotnet-shift";
    private const string Runtime = "dotnet";
    private const string ContainerName = "app";

    record ResourceProperties
    {
        [SetsRequiredMembers]
        public ResourceProperties(string name, string ns, string s2iImage)
        {
            AppName = Instance = DotnetAppName = DotnetComponent = name;
            Namespace = ns;
            DotnetS2iImage = s2iImage;
        }
        public required string AppName { get; init; }
        public required string Instance { get; init; }

        public required string DotnetAppName { get; init; }
        public required string DotnetS2iImage { get; init; }
        public required string DotnetComponent { get; init; }

        public required string Namespace { get; init; }

        public string DotnetAppImageName => DotnetAppName;
        public string DotnetBinaryBuildConfigName => DotnetAppName; // $"{DotnetAppName}-binary";
        public string ManagedBy => "dotnet-shift";
        public string DotnetRuntime => "dotnet";
    }

    public static readonly Argument<string> ProjectArgument =
        new Argument<string>("PROJECT", defaultValueFactory: () => ".", ".NET project to build");

    public DeployCommand() : base("deploy")
    {
        Add(ProjectArgument);

        this.SetHandler((project) => HandleAsync(project), ProjectArgument);
    }

    public static async Task<int> HandleAsync(string project)
    {
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

        // TODO: add some smarts (e.g. locate '.git'), and an option (--context).
        string contextDir = Path.GetDirectoryName(projectFile)!;

        // verify the project we're build is under the contextDir.
        if (!projectFullPath.StartsWith(contextDir))
        {
            Console.Error.WriteLine($"Project must be a subdirectory of the context directory.");
            return 1;
        }

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

        string name = projectInformation.AssemblyName.Replace(".", "-");
        string s2iImage = GetS2iImage(projectInformation.DotnetVersion);

        var client = new OpenShiftClient();
        string ns = client.Namespace;

        var props = new ResourceProperties(name, ns ,s2iImage);

        Console.WriteLine("Update DeploymentConfig");
        string deploymentConfig = GenerateDeploymentConfig(props);
        await client.ApplyDeploymentConfigAsync(deploymentConfig);

        Console.WriteLine("Update Service");
        string service = GenerateService(props);
        await client.ApplyServiceAsync(service);

        Console.WriteLine("Update ImageStream");
        string imageStream = GenerateImageStream(props);
        await client.ApplyImageStreamAsync(imageStream);

        Console.WriteLine("Update BuildConfig");
        string buildConfig = GenerateBinaryBuildConfig(props);
        await client.ApplyBuildConfigAsync(buildConfig);

        // TODO: add --expose option.
        Console.WriteLine("Update Route");
        string route = GenerateRoute(props);
        await client.ApplyRouteAsync(route);

        Console.WriteLine("Start build");
        using Stream archiveStream = CreateApplicationArchive(contextDir);
        await client.StartBinaryBuildAsync(props.DotnetBinaryBuildConfigName, archiveStream);

        // TODO: follow build.
        // TODO: print url.

        return 0;
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

    private static Stream CreateApplicationArchive(string directory)
    {
        // TODO: finetune PipeOptions.
        // TODO: remove folders like bin, obj.
        return Compress(TarStreamFromDirectory(directory));

        static Stream TarStreamFromDirectory(string directory)
        {
            var pipe = new Pipe();

            TarFileToPipeWriter(directory, pipe.Writer);

            return pipe.Reader.AsStream();

            static async void TarFileToPipeWriter(string directory, PipeWriter writer)
            {
                try
                {
                    await TarFile.CreateFromDirectoryAsync(directory, writer.AsStream(), includeBaseDirectory: false);
                    writer.Complete();
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                }
            }
        }

        static Stream Compress(Stream stream)
        {
            var pipe = new Pipe();

            StreamToPipeWriter(stream, pipe.Writer);

            return pipe.Reader.AsStream();

            static async void StreamToPipeWriter(Stream stream, PipeWriter writer)
            {
                try
                {
                    await stream.CopyToAsync(writer.AsStream());
                    writer.Complete();
                }
                catch (Exception ex)
                {
                    writer.Complete(ex);
                }
            }
        }
    }

    private static string GenerateDeploymentConfig(ResourceProperties properties)
    {
        return $$"""
        {
            "apiVersion": "apps.openshift.io/v1",
            "kind": "DeploymentConfig",
            "metadata": {
                "name": "{{properties.DotnetAppName}}",
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
                                "namespace": "{{properties.Namespace}}",
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
                            "kind": "DockerImage",
                            "name": "{{properties.DotnetS2iImage}}"
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
                      "app.kubernetes.io/managed-by": "{{properties.ManagedBy}}"
                    , "app.kubernetes.io/part-of": "{{properties.AppName}}"
                    , "app.kubernetes.io/name": "{{properties.DotnetAppName}}"
                    , "app.kubernetes.io/component": "{{properties.DotnetComponent}}"
                    , "app.kubernetes.io/instance": "{{properties.Instance}}"
        """;

        if (includeRuntimeLabels)
        {
            labels += $$"""
                    , "app.openshift.io/runtime": "{{properties.DotnetRuntime}}"
        """;
        }

        return labels;
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
