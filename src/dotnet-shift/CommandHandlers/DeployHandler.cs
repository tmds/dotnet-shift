namespace CommandHandlers;

using System;
using System.Runtime.InteropServices;
using OpenShift;

sealed partial class DeployHandler
{
    private ILogger Logger { get; }
    private IAnsiConsole Console { get; }
    private string WorkingDirectory { get; }
    private IOpenShiftClientFactory OpenShiftClientFactory { get; }
    private IProjectReader ProjectReader { get; }
    private IGitRepoReader GitRepoReader { get; }

    public DeployHandler(IAnsiConsole console, ILogger logger, string workingDirectory, IOpenShiftClientFactory clientFactory, IProjectReader projectReader, IGitRepoReader gitRepoReader)
    {
        Console = console;
        Logger = logger;
        WorkingDirectory = workingDirectory;
        OpenShiftClientFactory = clientFactory;
        ProjectReader = projectReader;
        GitRepoReader = gitRepoReader;
    }

    sealed class ComponentResources
    {
        public Deployment? Deployment { get; init; }
        public BuildConfig? BinaryBuildConfig { get; init; }
        public ConfigMap? ConfigMap { get; init; }
        public ImageStream? ImageStream { get; init; }
        public Route? Route { get; init; }
        public Service? Service { get; init; }
        public ImageStream? S2iImageStream { get; init; }
    }

    public async Task<int> ExecuteAsync(LoginContext login, string project, string? name, string? partOf, bool expose, CancellationToken cancellationToken)
    {
        // Find the .NET project file.
        if (!TryFindProjectFile(WorkingDirectory, project, out string? projectFile))
        {
            Console.WriteErrorLine($"Project '{project}' not found.");
            return CommandResult.Failure;
        }

        // Find the context directory.
        string projectFileDirectory = Path.GetDirectoryName(projectFile)!;
        string contextDir = FindContextDirectory(projectFileDirectory);

        // Read the .NET project.
        ProjectInformation projectInformation = ProjectReader.ReadProjectInfo(projectFile);
        if (!IsProjectInformationUsable(Console, projectInformation))
        {
            return CommandResult.Failure;
        }
        Debug.Assert(projectInformation.AssemblyName is not null);
        Debug.Assert(projectInformation.DotnetVersion is not null);

        string runtime = ResourceLabelValues.DotnetRuntime;
        string runtimeVersion = projectInformation.DotnetVersion;

        // Get git information.
        GitRepoInfo? gitInfo = GitRepoReader.ReadGitRepoInfo(contextDir);

        // Resource names.
        name ??= DefaultName(projectInformation.AssemblyName);
        string binaryBuildConfigName = GetBinaryConfigName(name);

        Console.WriteLine($"Using namespace '{login.Namespace}' at '{login.Server}'.");
        Console.WriteLine();
        IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);

        // Get the currently deployed resources.
        Console.WriteLine($"Retrieving existing resources...");
        ComponentResources resources = await GetDeployedResources(client, name, binaryBuildConfigName, runtime, cancellationToken);

        // If partOf was not set, default to the application of existing resources,
        // or the name of the deployment.
        partOf ??= GetPartOf(resources.Deployment?.Metadata?.Labels) ?? name;

        Console.WriteLine("Updating resources...");
        Console.WriteLine();
        resources = await UpdateResourcesAsync(client, resources, name, binaryBuildConfigName,
                                    runtime, runtimeVersion,
                                    gitUri: gitInfo?.RemoteUrl, gitRef: gitInfo?.RemoteBranch,
                                    partOf, expose, cancellationToken);

        // Start the build.
        // Ensure the s2i image is resolved.
        if (resources.S2iImageStream is not null &&
            !await CheckRuntimeImageAvailableAsync(client, resources.S2iImageStream, runtimeVersion, cancellationToken))
        {
            return CommandResult.Failure;
        }
        // Upload sources.
        Console.WriteLine($"Uploading sources from '{contextDir}'...");
        Build? build = await StartBuildAsync(client, binaryBuildConfigName, contextDir, projectFile, cancellationToken);

        bool follow = true;

        // Follow the build.
        if (follow)
        {
            string buildName = build.Metadata.Name;
            build = await FollowBuildAsync(client, buildName, cancellationToken);
            if (build is null)
            {
                Console.WriteErrorLine($"The build '{buildName}' is missing.");
                return CommandResult.Failure;
            }
            Debug.Assert(build.IsBuildFinished());
        }

        // Report build fail/success.
        if (!CheckBuildNotFailed(build, out string? builtImage))
        {
            return CommandResult.Failure;
        }

        // Follow the deployment.
        if (follow)
        {
            Debug.Assert(builtImage is not null);
            if (!await TryFollowDeploymentAsync(client, deploymentName: name, builtImage, cancellationToken))
            {
                return CommandResult.Failure;
            }
        }

        // Print Route url.
        if (resources.Route is { } route)
        {
            Console.WriteLine();
            Console.WriteLine($"The application is exposed at '{route.GetRouteUrl()}'");
        }

        return CommandResult.Success;
    }

    private bool CheckBuildNotFailed(Build build, out string? builtImage)
    {
        builtImage = null;

        string buildName = build.Metadata.Name;
        if (build.IsBuildFinished())
        {
            if (!build.IsBuildSuccess())
            {
                BuildCondition? failureCondition = build.Status.Conditions.FirstOrDefault(c => c.Type == build.Status.Phase);
                string withReason = DescribeConditionAsWith(failureCondition);
                switch (build.Status.Phase)
                {
                    case "Failed":
                        Console.WriteErrorLine($"The build '{buildName}' failed{withReason}.");
                        break;
                    case "Error":
                        Console.WriteErrorLine($"The build '{buildName}' failed to start{withReason}.");
                        break;
                    case "Cancelled":
                        Console.WriteErrorLine($"The build '{buildName}' was cancelled{withReason}.");
                        break;
                    default: // unknown phase
                        Console.WriteErrorLine($"The build '{buildName}' did not complete: {build.Status.Phase}{withReason}.");
                        break;
                }
                return false;
            }
            else
            {
                string imageReference = build.Status.OutputDockerImageReference;
                string imageDigest = build.Status.Output.To.ImageDigest;
                builtImage = $"{imageReference.Substring(0, imageReference.LastIndexOf(':'))}@{imageDigest}";
            }
        }

        return true;
    }

    private async Task<ComponentResources> GetDeployedResources(IOpenShiftClient client, string name, string? binaryBuildConfigName, string? runtime, CancellationToken cancellationToken)
    {
        Deployment? deployment = await client.GetDeploymentAsync(name, cancellationToken);
        BuildConfig? binaryBuildConfig = binaryBuildConfigName is null ? null : await client.GetBuildConfigAsync(binaryBuildConfigName, cancellationToken);
        ConfigMap? configMap = await client.GetConfigMapAsync(name, cancellationToken);
        ImageStream? imageStream = await client.GetImageStreamAsync(name, cancellationToken);
        Route? route = await client.GetRouteAsync(name, cancellationToken);
        Service? service = await client.GetServiceAsync(name, cancellationToken);
        ImageStream? s2iImageStream = runtime is null ? null : await client.GetImageStreamAsync(GetS2iImageStreamName(runtime), cancellationToken);

        return new ComponentResources()
        {
            Deployment = deployment,
            BinaryBuildConfig = binaryBuildConfig,
            ConfigMap = configMap,
            ImageStream = imageStream,
            Route = route,
            Service = service,
            S2iImageStream = s2iImageStream
        };
    }

    private static string GetS2iImageStreamName(string runtime) => runtime;

    private async Task<bool> TryFollowDeploymentAsync(IOpenShiftClient client, string deploymentName, string builtImage, CancellationToken cancellationToken)
    {
        bool isImageDeployed = false;
        DeploymentCondition2? previousProgressCondition = null, previousReplicaFailureCondition = null;

        bool printedImageNotYetDeployedShort = false;
        Stopwatch stopwatch = new();
        stopwatch.Start();

        while (true)
        {
            // Get the deployment.
            Deployment? deployment = await client.GetDeploymentAsync(deploymentName, cancellationToken);
            if (deployment is null)
            {
                Console.WriteErrorLine($"The deployment '{deploymentName}' is missing.");
                return false;
            }

            // Check if we're deploying the right image.
            string? deployedImage = deployment.Spec.Template.Spec.Containers.FirstOrDefault(c => c.Name == ContainerName)?.Image;
            if (!isImageDeployed)
            {
                isImageDeployed = deployedImage == builtImage;

                if (isImageDeployed)
                {
                    Console.WriteLine($"The image is being deployed.");
                }
            }
            else if (deployedImage != builtImage)
            {
                Console.WriteErrorLine($"The deployment '{deploymentName}' has changed to deploy image '{deployedImage}' instead of the image that was built.");
                return false;
            }

            // Wait for the image to get deployed.
            if (!isImageDeployed)
            {
                TimeSpan elapsed = stopwatch.Elapsed;

                // Print a message if the deployment doesn't pick up the built image after a short time.
                if (!printedImageNotYetDeployedShort &&
                    elapsed > TimeSpan.FromSeconds(5))
                {
                    printedImageNotYetDeployedShort = true;
                    Console.WriteLine($"Waiting for the deployment '{deploymentName}' to start deploying the image that was built...");
                }

                continue;
            }

            // Follow up on the deployment progess.
            Debug.Assert(isImageDeployed);
            if (deployment.Metadata.Generation == deployment.Status.ObservedGeneration)
            {
                DeploymentCondition2? progressCondition = deployment.Status.Conditions.FirstOrDefault(c => c.Type == "Progressing");
                DeploymentCondition2? replicaFailureCondition = deployment.Status.Conditions.FirstOrDefault(c => c.Type == "ReplicaFailure");

                // Check for progress.
                if (progressCondition?.Status == "True")
                {
                    if (progressCondition.Reason == "NewReplicaSetAvailable")
                    {
                        // Completed successfully.
                        Console.WriteLine($"The deployment finished successfully. There are {deployment.Status.AvailableReplicas} available pods.");
                        return true;
                    }

                    // Print a message if progress changes.
                    // if (HasConditionChanged(previousProgressCondition, progressCondition))
                    // {
                    //     Console.WriteLine($"The deployment is progressing{DescribeConditionAsWith(progressCondition)}.");
                    // }
                }

                // Report on replica set issues.
                if (replicaFailureCondition?.Status == "True")
                {
                    // Print a message if the error changes.
                    if (HasConditionChanged(previousReplicaFailureCondition, replicaFailureCondition))
                    {
                        Console.WriteErrorLine($"The deployment '{deploymentName}' replica set is failing{DescribeConditionAsWith(replicaFailureCondition)}.");
                    }
                }
                else if (replicaFailureCondition?.Status == "False")
                {
                    // Print a message when the error is gone.
                    if (previousReplicaFailureCondition?.Status == "True")
                    {
                        Console.WriteLine($"The deployment '{deploymentName}' replica set is no longer failing.");
                    }
                }

                // Check for no progress.
                if (progressCondition?.Status == "False")
                {
                    // Completed when no more progress.
                    Console.WriteErrorLine($"The deployment '{deploymentName}' failed{DescribeConditionAsWith(progressCondition)}.");
                    return false;
                }

                previousProgressCondition = progressCondition;
                previousReplicaFailureCondition = replicaFailureCondition;
            }

            await Task.Delay(100, cancellationToken);
        }

        static bool HasConditionChanged(DeploymentCondition2? previousCondition, DeploymentCondition2 newCondition)
            => previousCondition is null ||
                previousCondition.Status != newCondition.Status ||
                previousCondition.Reason != newCondition.Reason ||
                previousCondition.Message != newCondition.Message;
    }

    private static string DescribeConditionAsWith(BuildCondition? failureCondition)
    {
        string? reason = failureCondition?.Reason;
        string? message = failureCondition?.Message;
        string withReason = "";
        if (reason is not null)
        {
            withReason = $" with '{reason}'";
            if (message is not null)
            {
                withReason += $": \"{message}\"";
            }
        }

        return withReason;
    }

    private static string DescribeConditionAsWith(DeploymentCondition2? failureCondition)
    {
        string? reason = failureCondition?.Reason;
        string? message = failureCondition?.Message;
        string withReason = "";
        if (reason is not null)
        {
            withReason = $" with '{reason}'";
            if (message is not null)
            {
                withReason += $": \"{message}\"";
            }
        }

        return withReason;
    }

    private async Task<Build?> FollowBuildAsync(IOpenShiftClient client, string buildName, CancellationToken cancellationToken)
    {
        // Print the build log.
        Console.WriteLine("The build is running:");
        using Stream buildLog = await client.FollowBuildLogAsync(buildName, cancellationToken);
        StreamReader reader = new StreamReader(buildLog);
        string? line;
        while ((line = await ReadLineAsync(reader, cancellationToken)) != null)
        {
            // Omit empty lines so the build log appears contiguous in the output. 
            if (!line.AsSpan().Trim(' ').IsEmpty)
            {
                Console.WriteLine(line);
            }
        }
        Console.WriteLine();

        // Wait for the build to finish.
        while (true)
        {
            Build? build = await client.GetBuildAsync(buildName, cancellationToken);
            if (build is null)
            {
                return null;
            }
            if (build.IsBuildFinished())
            {
                return build;
            }
            await Task.Delay(100, cancellationToken);
        }
    }

    private async Task<Build> StartBuildAsync(IOpenShiftClient client, string binaryBuildConfigName, string contextDir, string projectFile, CancellationToken cancellationToken)
    {
        Dictionary<string, string> buildEnvironment = new();
        // Add DOTNET_STARTUP_PROJECT.
        AddStartupProject(buildEnvironment, contextDir, projectFile);

        using Stream archiveStream = CreateApplicationArchive(contextDir, buildEnvironment);

        return await client.StartBinaryBuildAsync(binaryBuildConfigName, archiveStream, cancellationToken);
    }

    private async static ValueTask<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
#if NET7_0_OR_GREATER
        return await reader.ReadLineAsync(cancellationToken);
#else
        Task<string?> lineTask = reader.ReadLineAsync();
        await lineTask.WaitAsync(cancellationToken);
        return await lineTask;
#endif
    }

    private async Task<ComponentResources> UpdateResourcesAsync(IOpenShiftClient client,
                                            ComponentResources current,
                                            string name,
                                            string binaryBuildConfigName,
                                            string runtime, string runtimeVersion,
                                            string? gitUri, string? gitRef,
                                            string partOf, bool expose,
                                            CancellationToken cancellationToken)
    {
        Dictionary<string, string> componentLabels = GetComponentLabels(partOf, name);
        Dictionary<string, string> runtimeLabels = GetRuntimeLabels(runtime, runtimeVersion);
        Dictionary<string, string> selectorLabels = GetSelectorLabels(name);

        string appImageStreamTagName = $"{name}:latest";

        // Order this reverse to the Delete order:
        // First BuildConfig, then DeploymentConfig, then the rest.
        BuildConfig binaryBuildConfig = await ApplyBinaryBuildConfig(
                                            client,
                                            binaryBuildConfigName,
                                            current.BinaryBuildConfig,
                                            appImageStreamTagName,
                                            s2iImageStreamTag: $"{GetS2iImageStreamName(runtime)}:{runtimeVersion}",
                                            Merge(componentLabels, runtimeLabels),
                                            cancellationToken);

        Deployment? deployment = await ApplyAppDeployment(
                                        client,
                                        name,
                                        current.Deployment,
                                        appImageStreamTagName,
                                        gitUri, gitRef,
                                        Merge(componentLabels, runtimeLabels),
                                        selectorLabels,
                                        cancellationToken
                                    );

        ConfigMap? configMap = await ApplyAppConfigMap(
                                    client,
                                    name,
                                    current.ConfigMap,
                                    componentLabels,
                                    cancellationToken);

        Debug.Assert(runtime == ResourceLabelValues.DotnetRuntime);
        ImageStream? s2iImageStream = await ApplyDotnetImageStreamTag(
                                        client,
                                        current.S2iImageStream,
                                        runtimeVersion,
                                        cancellationToken);

        ImageStream? imageStream = await ApplyAppImageStream(client,
                                  name,
                                  current.ImageStream,
                                  componentLabels,
                                  cancellationToken);

        Route? route = null;
        if (expose ||
            current.Route is not null) // Update the route when the application was already exposed.
        {
            route = await ApplyAppRoute(client,
                                name,
                                current.Route,
                                serviceName: name,
                                componentLabels,
                                cancellationToken);
        }

        Service service = await ApplyAppService(client,
                              name,
                              current.Service,
                              componentLabels,
                              selectorLabels,
                              cancellationToken);

        return new ComponentResources()
        {
            Deployment = deployment,
            BinaryBuildConfig = binaryBuildConfig,
            ConfigMap = configMap,
            ImageStream = imageStream,
            Route = route,
            Service = service,
            S2iImageStream = s2iImageStream
        };
    }

    private string? GetPartOf(IDictionary<string, string>? labels)
    {
        string? partOf = null;
        labels?.TryGetValue("app.kubernetes.io/part-of", out partOf);
        return partOf;
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> dict1, Dictionary<string, string> dict2)
        => new[] { dict1, dict2 }.SelectMany(d => d).ToDictionary(p => p.Key, p => p.Value);

    private static Dictionary<string, string> GetComponentLabels(string appName, string name)
    {
        Dictionary<string, string> labels = new();

        labels[ResourceLabels.ManagedBy] = ResourceLabelValues.ManagedByDotnetShift;

        labels[ResourceLabels.PartOf] = appName;
        labels[ResourceLabels.Instance] = appName; // I guess.

        labels[ResourceLabels.Name] = name;

        // This label is a description of what the component is for.
        labels[ResourceLabels.Component] = name;

        return labels;
    }

    private static Dictionary<string, string> GetRuntimeLabels(string runtime, string runtimeVersion)
    {
        Dictionary<string, string> labels = new();

        labels[ResourceLabels.Runtime] = runtime;

        return labels;
    }

    private static Dictionary<string, string> GetSelectorLabels(string name)
    {
        Dictionary<string, string> labels = new();

        labels["app"] = name;

        return labels;
    }

    private static string DefaultName(string assemblyName)
    {
        string name = assemblyName;

        // a lowercase RFC 1123 subdomain must consist of lower case alphanumeric characters, '-' or '.', and must start and end with an alphanumeric character
        name = name.Replace(".", "-").ToLowerInvariant();

        return name;
    }

    internal static bool IsProjectInformationUsable(IAnsiConsole Console, ProjectInformation projectInformation)
    {
        bool usable = true;

        if (projectInformation.DotnetVersion is null)
        {
            Console.WriteErrorLine($"Cannot determine project target framework version.");
            usable = false;
        }

        if (projectInformation.AssemblyName is null)
        {
            Console.WriteErrorLine($"Cannot determine application assembly name.");
            usable = false;
        }

        return usable;
    }

    private static void AddStartupProject(Dictionary<string, string> buildEnvironment, string contextDir, string projectFile)
    {
        string dotnetStartupProject = projectFile.Substring(contextDir.Length).TrimStart(Path.DirectorySeparatorChar);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            dotnetStartupProject = dotnetStartupProject.Replace('\\', '/');
        }
        buildEnvironment["DOTNET_STARTUP_PROJECT"] = dotnetStartupProject;
    }

    internal static bool TryFindProjectFile(string workingDirectory, string projectPath, [NotNullWhen(true)] out string? projectFile)
    {
        string projectFullPath = Path.Combine(workingDirectory, projectPath);
        projectFile = null;
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
        return projectFile is not null;
    }

    internal static string FindContextDirectory(string path)
    {
        // Guess what would be an appropriate context directory.
        string? contextDir = null;

        // Move up directory by directory util we we have a .git subdirectory.
        contextDir = FindSelfOrParent(path, d => Directory.Exists(Path.Combine(d, ".git")));

        if (contextDir is null)
        {
            // Move up directory by directory util we we have a *.sln file.
            contextDir = FindSelfOrParent(path, d => Directory.Exists(d) && Directory.GetFiles(d, "*.sln").Length > 0);
        }

        // Avoid using '/tmp' and '~'.
        if (contextDir == Path.GetTempPath() ||
            contextDir == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        {
            contextDir = null;
        }

        // Default to the project file directory.
        if (contextDir is null)
        {
            contextDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        }

        return contextDir!;

        static string? FindSelfOrParent(string path, Func<string, bool> predicate)
        {
            string current = path;
            do
            {
                if (predicate(current))
                {
                    return current;
                }
                current = Path.GetDirectoryName(current)!;
                if (current == Path.GetPathRoot(current))
                {
                    return null;
                }
            } while (true);
        }
    }

    private string GetBinaryConfigName(string componentName) => $"{componentName}-binary";
}