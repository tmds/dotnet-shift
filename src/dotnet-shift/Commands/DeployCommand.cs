using System.CommandLine;
using System.Formats.Tar;
using System.IO.Pipelines;

sealed class DeployCommand : Command
{
    private const string DotnetShift = "dotnet-shift";
    private const string Runtime = "dotnet";
    private const string ContainerName = "app";

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

        Console.WriteLine("Update DeploymentConfig");
        string deploymentConfig = GenerateDeploymentConfig(ns, name);
        await client.ApplyDeploymentConfigAsync(deploymentConfig);

        Console.WriteLine("Update Service");
        string service = GenerateService(ns, name);
        await client.ApplyServiceAsync(service);

        Console.WriteLine("Update ImageStream");
        string imageStream = GenerateImageStream(ns, name);
        await client.ApplyImageStreamAsync(imageStream);

        Console.WriteLine("Update BuildConfig");
        string buildConfig = GenerateBinaryBuildConfig(ns, name, s2iImage);
        await client.ApplyBuildConfigAsync(buildConfig);

        // TODO: add --expose option.
        Console.WriteLine("Update Route");
        string route = GenerateRoute(ns, name);
        await client.ApplyRouteAsync(route);

        Console.WriteLine("Start build.");
        string binaryBuildConfigName = BinaryBuildConfigName(name);
        using Stream archiveStream = CreateApplicationArchive(contextDir);
        await client.StartBinaryBuildAsync(binaryBuildConfigName, archiveStream);

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

    private static string GenerateDeploymentConfig(string ns, string name)
    {
        return $$"""
        {
            "apiVersion": "apps.openshift.io/v1",
            "kind": "DeploymentConfig",
            "metadata": {
                "name": "{{name}}",
                "labels": {
                    "app.kubernetes.io/managed-by": "{{DotnetShift}}",

                    "app.kubernetes.io/name": "{{name}}",
                    "app.kubernetes.io/component": "{{name}}",
                    "app.kubernetes.io/instance": "{{name}}",

                    "app.openshift.io/runtime": "{{Runtime}}"
                }
            },
            "spec": {
                "replicas": 1,
                "selector": {
                    "app.kubernetes.io/name": "{{name}}"
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
                                "namespace": "{{ns}}",
                                "name": "{{name}}:latest"
                            }
                        }
                    }
                ],
                "template": {
                    "metadata": {
                        "labels": {
                            "app.kubernetes.io/name": "{{name}}",
                            "app.kubernetes.io/managed-by": "{{DotnetShift}}"
                        }
                    },
                    "spec": {
                        "containers": [
                            {
                                "name": "{{ContainerName}}",
                                "image": "{{name}}",
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

    private static string GenerateService(string ns, string name)
    {
        return $$"""
        {
            "apiVersion": "v1",
            "kind": "Service",
            "spec": {
                "type": "ClusterIP",
                "selector": {
                    "app.kubernetes.io/name": "{{name}}"
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
                "name": "{{name}}",
                "labels": {
                    "app.kubernetes.io/managed-by": "{{DotnetShift}}",
                    "app.kubernetes.io/name": "{{name}}",
                    "app.kubernetes.io/component": "{{name}}",
                    "app.kubernetes.io/instance": "{{name}}"
                }
            }
        }
        """;
    }

    private static string GenerateImageStream(string ns, string name)
    {
        return $$"""
        {
            "apiVersion": "image.openshift.io/v1",
            "kind": "ImageStream",
            "metadata": {
                "labels": {
                    "app.kubernetes.io/managed-by": "{{DotnetShift}}",
                    "app.kubernetes.io/name": "{{name}}",
                    "app.kubernetes.io/component": "{{name}}",
                    "app.kubernetes.io/instance": "{{name}}"
                },
                "name": "{{name}}"
            }
        }
        """;
    }

    private static string BinaryBuildConfigName(string name) => $"{name}-binary";

    private static string GenerateBinaryBuildConfig(string ns, string name, string s2iImage)
    {
        return $$"""
        {
            "apiVersion": "build.openshift.io/v1",
            "kind": "BuildConfig",
            "metadata": {
                "labels": {
                    "app.kubernetes.io/managed-by": "{{DotnetShift}}",

                    "app.kubernetes.io/name": "{{name}}",
                    "app.kubernetes.io/component": "{{name}}",
                    "app.kubernetes.io/instance": "{{name}}",

                    "app.openshift.io/runtime": "{{Runtime}}"
                },
                "name": "{{BinaryBuildConfigName(name)}}"
            },
            "spec": {
                "failedBuildsHistoryLimit": 5,
                "successfulBuildsHistoryLimit": 5,
                "output": {
                    "to": {
                        "kind": "ImageStreamTag",
                        "name": "{{name}}:latest"
                    }
                },
                "source": {
                    "type": "Binary"
                },
                "strategy": {
                    "sourceStrategy": {
                        "from": {
                            "kind": "DockerImage",
                            "name": "{{s2iImage}}"
                        }
                    },
                    "type": "Source"
                }
            }
        }
        """;
    }

    private static string GenerateRoute(string ns, string name)
    {
        return $$"""
        {
            "apiVersion": "route.openshift.io/v1",
            "kind": "Route",
            "spec": {
                "to": {
                    "kind": "Service",
                    "name": "{{name}}"
                },
                "port": {
                    "targetPort": 8080
                }
            },
            "metadata": {
                "name": "{{name}}",
                "labels": {
                    "app.kubernetes.io/managed-by": "{{DotnetShift}}",

                    "app.kubernetes.io/name": "{{name}}",
                    "app.kubernetes.io/component": "{{name}}",
                    "app.kubernetes.io/instance": "{{name}}"
                }
            }
        }
        """;
    }
}
