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
        if (contextDir != projectFileDirectory)
        {
            Console.WriteLine($"Using context directory '{contextDir}'");
        }

        // Read the .NET project.
        ProjectInformation projectInformation = ProjectReader.ReadProjectInfo(projectFile);
        if (!IsProjectInformationUsable(Console, projectInformation))
        {
            return CommandResult.Failure;
        }
        Debug.Assert(projectInformation.AssemblyName is not null);
        Debug.Assert(projectInformation.DotnetVersion is not null);

        // Get git information.
        GitRepoInfo? gitInfo = GitRepoReader.ReadGitRepoInfo(contextDir);

        // Resource names.
        name ??= DefaultName(projectInformation.AssemblyName);
        string binaryBuildConfigName = $"{name}-binary";

        IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);

        // Get the currently deployed resources.
        string? currentPartOf = null;
        Deployment? currentDeployment = await client.GetDeploymentAsync(name, cancellationToken);
        currentPartOf ??= GetPartOf(currentDeployment?.Metadata?.Labels);
        BuildConfig? currentBuildConfig = await client.GetBuildConfigAsync(binaryBuildConfigName, cancellationToken);
        ConfigMap? currentConfigMap = await client.GetConfigMapAsync(name, cancellationToken);
        ImageStream? currentAppImageStream = await client.GetImageStreamAsync(name, cancellationToken);
        Route? currentRoute = await client.GetRouteAsync(name, cancellationToken);
        Service? currentService = await client.GetServiceAsync(name, cancellationToken);
        ImageStream? currentDotnetImageStream = await client.GetImageStreamAsync(DotnetImageStreamName, cancellationToken);

        // Update the route when the application was already exposed.
        expose = expose || currentRoute is not null;
        // If partOf was not set, default to the application of existing resources,
        // or the name of the deployment.
        partOf ??= currentPartOf ?? name;

        Dictionary<string, string> componentLabels = GetComponentLabels(partOf, name);
        Dictionary<string, string> dotnetLabels = GetDotnetLabels();
        Dictionary<string, string> selectorLabels = GetSelectorLabels(name);
        string appImageStreamTagName = $"{name}:latest";
        string dotnetVersion = projectInformation.DotnetVersion;

        Console.WriteLine("Updating resources");
        await UpdateResourcesAsync(client,
                                   name,
                                   binaryBuildConfigName,
                                   appImageStreamTagName,
                                   dotnetVersion,
                                   currentDeployment,
                                   currentBuildConfig,
                                   currentConfigMap,
                                   currentAppImageStream,
                                   currentRoute,
                                   currentService,
                                   currentDotnetImageStream,
                                   expose,
                                   gitUri: gitInfo?.RemoteUrl, gitRef: gitInfo?.RemoteBranch,
                                   componentLabels,
                                   dotnetLabels,
                                   selectorLabels,
                                   cancellationToken);

        Build? build = await StartBuildAsync(client, binaryBuildConfigName, contextDir, projectFile, cancellationToken);
        string buildName = build.Metadata.Name;
        bool follow = true;

        if (follow)
        {
            build = await FollowBuildAsync(client, buildName, cancellationToken);
            if (build is null)
            {
                Console.WriteErrorLine($"The build '{buildName}' is missing.");
                return CommandResult.Failure;
            }
            Debug.Assert(build.IsBuildFinished());
        }

        string? builtImage = null;
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
                return CommandResult.Failure;
            }
            else
            {
                string imageReference = build.Status.OutputDockerImageReference;
                string imageDigest = build.Status.Output.To.ImageDigest;
                builtImage = $"{imageReference.Substring(0, imageReference.LastIndexOf(':'))}@{imageDigest}";
                Console.WriteLine($"The build '{buildName}' succesfully built '{builtImage}'.");
            }
        }

        if (follow)
        {
            Debug.Assert(builtImage is not null);
            if (!await TryFollowDeploymentAsync(client, deploymentName: name, builtImage, cancellationToken))
            {
                return CommandResult.Failure;
            }
        }

        // Print Route url.
        if (expose)
        {
            Route? route = await client.GetRouteAsync(name, cancellationToken);
            if (route is not null)
            {
                Console.WriteLine($"The application can be reached at '{route.GetRouteUrl()}'");
            }
        }

        return CommandResult.Success;
    }

    private async Task<bool> TryFollowDeploymentAsync(IOpenShiftClient client, string deploymentName, string builtImage, CancellationToken cancellationToken)
    {
        bool isImageDeployed = false;
        bool printedImageNotYetDeployedShort = false;
        DeploymentCondition2? previousProgressCondition = null, previousReplicaFailureCondition = null;

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
                    Console.WriteLine($"The deployment '{deploymentName}' is deploying the image that was built.");
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
                        Console.WriteLine($"The deployment completed successfully.");
                        return true;
                    }

                    // Print a message if progress changes.
                    if (HasConditionChanged(previousProgressCondition, progressCondition))
                    {
                        Console.WriteLine($"The deployment is progressing{DescribeConditionAsWith(progressCondition)}.");
                    }
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

            await Task.Delay(100);
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
        Console.WriteLine();
        Console.WriteLine("Build log:");
        using Stream buildLog = await client.FollowBuildLogAsync(buildName, cancellationToken);
        StreamReader reader = new StreamReader(buildLog);
        string? line;
        while ((line = await ReadLineAsync(reader, cancellationToken)) != null)
        {
            Console.WriteLine(line);
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
        Console.WriteLine("Uploading sources for build");

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

    private async Task UpdateResourcesAsync(IOpenShiftClient client,
                                            string name,
                                            string binaryBuildConfigName,
                                            string appImageStreamTagName,
                                            string dotnetVersion,
                                            Deployment? currentDeployment,
                                            BuildConfig? currentBuildConfig,
                                            ConfigMap? currentConfigMap,
                                            ImageStream? currentAppImageStream,
                                            Route? currentRoute,
                                            Service? currentService,
                                            ImageStream? currentDotnetImageStream,
                                            bool expose,
                                            string? gitUri, string? gitRef,
                                            Dictionary<string, string> componentLabels,
                                            Dictionary<string, string> dotnetLabels,
                                            Dictionary<string, string> selectorLabels,
                                            CancellationToken cancellationToken)
    {
        // Order this reverse to the Delete order:
        // First BuildConfig, then DeploymentConfig, then the rest.
        await ApplyBinaryBuildConfig(
            client,
            binaryBuildConfigName,
            currentBuildConfig,
            appImageStreamTagName,
            s2iImageStreamTag: $"{DotnetImageStreamName}:{dotnetVersion}",
            Merge(componentLabels, dotnetLabels),
            cancellationToken);
        await ApplyAppDeployment(
            client,
            name,
            currentDeployment,
            appImageStreamTagName,
            gitUri, gitRef,
            Merge(componentLabels, dotnetLabels),
            selectorLabels,
            cancellationToken
        );

        if (currentConfigMap is null)
        {
            Console.WriteLine($"Creating ConfigMap '{name}'");
            await CreateAppConfigMap(client,
                                    name,
                                    componentLabels,
                                    cancellationToken);
        }

        await ApplyDotnetImageStreamTag(client,
                                        currentDotnetImageStream,
                                        dotnetVersion,
                                        cancellationToken);

        await ApplyAppImageStream(client,
                                  name,
                                  currentAppImageStream,
                                  componentLabels,
                                  cancellationToken);

        if (expose)
        {
            await ApplyAppRoute(client,
                                name,
                                currentRoute,
                                serviceName: name,
                                componentLabels,
                                cancellationToken);
        }

        await ApplyAppService(client,
                              name,
                              currentService,
                              componentLabels,
                              selectorLabels,
                              cancellationToken);
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

    private static Dictionary<string, string> GetDotnetLabels()
    {
        Dictionary<string, string> labels = new();

        labels[ResourceLabels.Runtime] = ResourceLabelValues.DotnetRuntime;

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
}