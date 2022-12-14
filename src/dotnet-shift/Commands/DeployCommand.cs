using System.CommandLine;
using System.IO;
using System.IO.Pipelines;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
#if NET6_0
using System.IO.Compression;
#else
using System.Formats.Tar;
#endif

sealed class DeployCommand : Command
{
    private const string DotnetShift = "dotnet-shift";
    private const string Runtime = "dotnet";
    private const string ContainerName = "app";

    record ResourceProperties
    {
        [SetsRequiredMembers]
        public ResourceProperties(string name, string s2iImage)
        {
            AppName = Instance = DotnetAppName = DotnetComponent = name;
            DotnetS2iImage = s2iImage;
        }
        public required string AppName { get; init; }
        public required string Instance { get; init; }

        public required string DotnetAppName { get; init; }
        public required string DotnetS2iImage { get; init; }
        public required string DotnetComponent { get; init; }

        public string DotnetAppImageName => DotnetAppName;
        public string DotnetBinaryBuildConfigName => $"{DotnetAppName}-binary";
        public string ManagedBy => "dotnet-shift";
        public string DotnetRuntime => "dotnet";
    }

    public static readonly Argument<string> ProjectArgument =
        new Argument<string>("PROJECT", defaultValueFactory: () => ".", ".NET project to build");

    public static readonly Option<string> AsFileOption =
        new Option<string>( "--as-file", "Generates a JSON file with resources.");

    public DeployCommand() : base("deploy", "Deploys .NET application")
    {
        Add(ProjectArgument);
        Add(AsFileOption);

        this.SetHandler((project, asFile) => HandleAsync(project, asFile), ProjectArgument, AsFileOption);
    }

    public static async Task<int> HandleAsync(string project, string? asFile)
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

        var props = new ResourceProperties(name ,s2iImage);

        string deploymentConfig = GenerateDeploymentConfig(props);
        string service = GenerateService(props);
        string imageStream = GenerateImageStream(props);
        string buildConfig = GenerateBinaryBuildConfig(props);
        // TODO: add --expose option.
        string route = GenerateRoute(props);

        if (asFile is null)
        {
            var client = new OpenShiftClient();

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
            using Stream archiveStream = CreateApplicationArchive(contextDir);
            await client.StartBinaryBuildAsync(props.DotnetBinaryBuildConfigName, archiveStream);

            // TODO: follow build.
            // TODO: print url.
        }
        else
        {
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

#if NET6_0
    private static Stream CreateApplicationArchive(string directory)
    {
        // ZipFile doesn't support creating a user-only file in /tmp, so create it under LocalApplicationData instead.
        string appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        string appData = Path.Combine(appDataRoot, DotnetShift);
        string appDataTmp = Path.Combine(appData, "tmp");
        Directory.CreateDirectory(appDataTmp);

        string zipFileName = Path.Combine(appDataTmp, Path.GetRandomFileName());
        try
        {
            // TODO: remove folders like bin, obj.
            ZipFile.CreateFromDirectory(directory, zipFileName, CompressionLevel.Fastest, includeBaseDirectory: false);

            // Set DeleteOnClose.
            var handle = File.OpenHandle(zipFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.DeleteOnClose);
            return new FileStream(handle, FileAccess.Read);
        }
        catch
        {
            try
            {
                File.Delete(zipFileName);
            }
            catch
            { }
            throw;
        }
    }
#else
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
#endif

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
