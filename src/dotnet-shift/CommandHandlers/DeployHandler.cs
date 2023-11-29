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

    // Let the user know something didn't happen after a short time.
    private TimeSpan ShortFeedbackTimeout => TimeSpan.FromSeconds(5);

    // Fail the operation if there was no progress for a long time.
    private TimeSpan NoProgressTimeout => TimeSpan.FromSeconds(60);

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
        public required Dictionary<string, ConfigMap> ConfigMaps { get; init; }
        public ImageStream? ImageStream { get; init; }
        public Route? Route { get; init; }
        public Service? Service { get; init; }
        public ImageStream? S2iImageStream { get; init; }
        public required Dictionary<string, PersistentVolumeClaim> PersistentVolumeClaims { get; init; }
    }

    public async Task<int> ExecuteAsync(LoginContext login, string project, string? name, string? partOf, bool expose, bool follow, bool doBuild, CancellationToken cancellationToken)
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
        if (!ProjectReader.TryReadProjectInfo(projectFile, out ProjectInformation? projectInfo, out List<string> validationErrors))
        {
            foreach (var error in validationErrors)
            {
                Console.WriteErrorLine(error);
            }
            return CommandResult.Failure;
        }
        Debug.Assert(projectInfo.AssemblyName is not null);
        Debug.Assert(projectInfo.DotnetVersion is not null);

        string runtime = ResourceLabelValues.DotnetRuntime;
        string runtimeVersion = projectInfo.DotnetVersion;

        // Get git information.
        GitRepoInfo? gitInfo = GitRepoReader.ReadGitRepoInfo(contextDir);

        // Resource names.
        name ??= DefaultName(projectInfo.AssemblyName);
        string binaryBuildConfigName = GetBinaryConfigName(name);

        Console.WriteLine($"Deploying '{name}' to namespace '{login.Namespace}' at '{login.Server}'.");

        using IOpenShiftClient client = OpenShiftClientFactory.CreateClient(login);

        // Get the currently deployed resources.
        Console.WriteLine(); // section "resources"
        Console.WriteLine($"Retrieving existing resources...");
        List<string> storageNames = projectInfo.VolumeClaims.Select(claim => GetResourceNameFor(name, claim)).ToList();
        List<string> mapNames = projectInfo.ConfigMaps.Select(map => GetResourceNameFor(name, map)).ToList();
        ComponentResources resources = await GetDeployedResources(client,
            name, binaryBuildConfigName, storageNames, mapNames, runtime, cancellationToken);

        // If partOf was not set, default to the application of existing resources,
        // or the name of the deployment.
        partOf ??= GetPartOf(resources.Deployment?.Metadata?.Labels) ?? name;

        // Update the route if the app is already exposed.
        expose |= resources.Route is not null;
        if (expose && projectInfo.ExposedPort is null)
        {
            if (projectInfo.ContainerPorts.Length == 0)
            {
                Console.WriteErrorLine("The project has no container ports. It's not clear what port to expose.");
            }
            else
            {
                Console.WriteErrorLine("The project has multiple container ports. It's not clear what port to expose.");
            }
            return CommandResult.Failure;
        }

        if (!doBuild && resources.Deployment is null)
        {
            Console.WriteErrorLine($"The build can not be skipped on the first deployment.");
            return CommandResult.Failure;
        }

        string? appImageStreamTagName = $"{name}:latest";

        Console.WriteLine(); // section "build"
        Console.WriteLine("Updating build resources...");
        await UpdateBuildResourcesAsync(
                                client, resources, name, binaryBuildConfigName, appImageStreamTagName,
                                runtime, runtimeVersion, partOf, cancellationToken);
        string? appImage = null;
        if (doBuild)
        {
            // Ensure the s2i image is resolved.
            if (resources.S2iImageStream is not null &&
                !await CheckRuntimeImageAvailableAsync(client, resources.S2iImageStream, runtimeVersion, cancellationToken))
            {
                return CommandResult.Failure;
            }

            // Upload sources and start the build.
            Console.WriteLine($"Uploading sources from directory '{contextDir}'...");
            Build? build = await StartBuildAsync(client, binaryBuildConfigName, contextDir, projectFile,
                                                 projectInfo.ContainerEnvironmentVariables, cancellationToken);

            // Follow the build.
            string buildName = build.Metadata.Name;
            build = await FollowBuildAsync(client, buildName, cancellationToken);
            if (build is null)
            {
                Console.WriteErrorLine($"The build '{buildName}' is missing.");
                return CommandResult.Failure;
            }
            Debug.Assert(build.IsBuildFinished());

            // Report build fail/success.
            if (!CheckBuildNotFailed(build, out appImage))
            {
                return CommandResult.Failure;
            }
            Debug.Assert(appImage is not null);
        }
        else
        {
            Console.WriteLine("The image build is skipped for this deployment.");
        }

        Console.WriteLine(); // section "deployment"
        Console.WriteLine("Updating deployment resources...");
        Route? route = await UpdateResourcesAsync(
                                    client, resources, name,
                                    runtime, runtimeVersion,
                                    gitUri: gitInfo?.RemoteUrl, gitRef: gitInfo?.RemoteBranch,
                                    appImage, appImageStreamTagName,
                                    partOf, expose, projectInfo.ContainerPorts, projectInfo.ExposedPort,
                                    projectInfo.VolumeClaims, projectInfo.ConfigMaps,
                                    projectInfo.ContainerLimits, cancellationToken);

        // Follow the deployment.
        if (follow)
        {
            if (!await TryFollowDeploymentAsync(client, deploymentName: name, appImage, cancellationToken))
            {
                return CommandResult.Failure;
            }
        }

        // Print Route url.
        if (route is not null)
        {
            Console.WriteLine(); // section "route"
            Console.WriteLine($"The application is exposed at '{route.GetRouteUrl()}'.");
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
                BuildCondition? failureCondition = build.Status.Conditions?.FirstOrDefault(c => c.Type == build.Status.Phase);
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

    private async Task<ComponentResources> GetDeployedResources(IOpenShiftClient client,
        string name, string? binaryBuildConfigName, List<string> pvcNames, List<string> configMapNames,
        string? runtime, CancellationToken cancellationToken)
    {
        Deployment? deployment = await client.GetDeploymentAsync(name, cancellationToken);
        BuildConfig? binaryBuildConfig = binaryBuildConfigName is null ? null : await client.GetBuildConfigAsync(binaryBuildConfigName, cancellationToken);
        ConfigMap? configMap = await client.GetConfigMapAsync(name, cancellationToken);
        ImageStream? imageStream = await client.GetImageStreamAsync(name, cancellationToken);
        Route? route = await client.GetRouteAsync(name, cancellationToken);
        Service? service = await client.GetServiceAsync(name, cancellationToken);
        ImageStream? s2iImageStream = runtime is null ? null : await client.GetImageStreamAsync(GetS2iImageStreamName(runtime), cancellationToken);
        Dictionary<string, PersistentVolumeClaim> pvcs = new();
        foreach (var pvcName in pvcNames)
        {
            PersistentVolumeClaim? claim = await client.GetPersistentVolumeClaimAsync(pvcName, cancellationToken);
            if (claim is not null)
            {
                pvcs.Add(pvcName, claim);
            }
        }
        Dictionary<string, ConfigMap> maps = new();
        foreach (var mapName in configMapNames)
        {
            ConfigMap? map = await client.GetConfigMapAsync(mapName, cancellationToken);
            if (map is not null)
            {
                maps.Add(mapName, map);
            }
        }

        return new ComponentResources()
        {
            Deployment = deployment,
            BinaryBuildConfig = binaryBuildConfig,
            ConfigMaps = maps,
            ImageStream = imageStream,
            Route = route,
            Service = service,
            S2iImageStream = s2iImageStream,
            PersistentVolumeClaims = pvcs,
        };
    }

    private static string GetResourceNameFor(string componentName, PersistentStorage storage)
        => $"{componentName}-{storage.Name}";

    private static string GetResourceNameFor(string componentName, ConfMap confMap)
        => $"{componentName}-{confMap.Name}";

    private static string GetS2iImageStreamName(string runtime) => runtime;

    private async Task<bool> TryFollowDeploymentAsync(IOpenShiftClient client, string deploymentName, string? builtImage, CancellationToken cancellationToken)
    {
        // Wait for the 'spec' to get updated to deploy the image.
        bool isImageDeployed = false;
        bool printedImageNotYetDeployed = false;
        Stopwatch imageDeployStopwatch = Stopwatch.StartNew();

        // Wait for the deployment 'status' to report on the 'spec' generation.
        bool printedPreviousGenerationStillDeploying = false;
        Stopwatch? generationStopwatch = null;
        Stopwatch? noProgressStopwatch = null;
        DeploymentCondition2? previousGenerationProgressCondition = null, previousGenerationReplicaFailureCondition = null;

        // Wait for the deployment 'spec' to complete.
        DeploymentCondition2? previousProgressCondition = null, previousReplicaFailureCondition = null;

        while (true)
        {
            // Get the deployment.
            Deployment? deployment = await client.GetDeploymentAsync(deploymentName, cancellationToken);
            if (deployment is null)
            {
                Console.WriteErrorLine($"The deployment '{deploymentName}' is missing.");
                return false;
            }

            if (builtImage is not null)
            {
                // Check if we're deploying the builtImage.
                string? deployedImage = deployment.Spec.Template.Spec.Containers?.FirstOrDefault(c => c.Name == ContainerName)?.Image;
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
                    TimeSpan elapsed = imageDeployStopwatch.Elapsed;

                    // Print a message if the deployment doesn't pick up the built image after a short time.
                    if (!printedImageNotYetDeployed &&
                        elapsed > ShortFeedbackTimeout)
                    {
                        printedImageNotYetDeployed = true;
                        Console.WriteLine($"Waiting for the deployment '{deploymentName}' to start deploying the image that was built...");
                    }
                    else if (elapsed > NoProgressTimeout)
                    {
                        Console.WriteLine($"The deployment hasn't started deploying the image after {Math.Round(elapsed.TotalSeconds)} seconds.");
                        return false;
                    }
                }
            }
            else
            {
                isImageDeployed = true;
            }

            if (isImageDeployed)
            {
                // Wait for the status to match the spec generation.
                if (deployment.Metadata.Generation != deployment.Status.ObservedGeneration)
                {
                    // Time how long we're waiting for the generation to change.
                    if (generationStopwatch is null)
                    {
                        generationStopwatch = Stopwatch.StartNew();
                    }

                    // If the generation doesn't change after a short time.
                    TimeSpan elapsed = imageDeployStopwatch.Elapsed;
                    if (elapsed > ShortFeedbackTimeout)
                    {
                        DeploymentCondition2? progressCondition = deployment.Status.Conditions?.FirstOrDefault(c => c.Type == "Progressing");
                        DeploymentCondition2? replicaFailureCondition = deployment.Status.Conditions?.FirstOrDefault(c => c.Type == "ReplicaFailure");

                        // Report on replica set issues.
                        if (replicaFailureCondition?.Status == "True")
                        {
                            // Print a message if the error changes.
                            if (HasConditionChanged(previousGenerationReplicaFailureCondition, replicaFailureCondition))
                            {
                                printedPreviousGenerationStillDeploying = true;
                                Console.WriteErrorLine($"The previous deployment of '{deploymentName}' replica set is failing{DescribeConditionAsWith(replicaFailureCondition)}.");
                            }
                        }
                        else if (replicaFailureCondition?.Status == "False")
                        {
                            // Print a message when the error is gone.
                            if (previousGenerationReplicaFailureCondition?.Status == "True")
                            {
                                Console.WriteLine($"The previous deployment of '{deploymentName}' replica set is no longer failing.");
                            }
                        }

                        // Report on progress issues. Fail when we're no longer making progress.
                        if (progressCondition?.Status == "True")
                        {
                            // Print a message when the error is gone.
                            if (previousGenerationProgressCondition?.Status == "False")
                            {
                                Console.WriteLine($"The previous deployment of '{deploymentName}' is progressing.");
                            }

                            // Reset the fail timer when we make some progress.
                            noProgressStopwatch?.Restart();
                        }
                        else
                        {
                            progressCondition ??= new()
                            {
                                Reason = "Unknown",
                                Message = null
                            };

                            // Print a message if the error changes.
                            if (HasConditionChanged(previousGenerationProgressCondition, progressCondition))
                            {
                                printedPreviousGenerationStillDeploying = true;
                                Console.WriteErrorLine($"The previous deployment of '{deploymentName}' is not progressing{DescribeConditionAsWith(progressCondition)}.");
                            }

                            // Consider 'ReplicaSetCreateError' as unrecoverable immediately.
                            if (progressCondition.Reason == "ReplicaSetCreateError")
                            {
                                return false;
                            }
                            // For other reasons, wait some time to consider the condition unrecoverable.
                            if (noProgressStopwatch?.Elapsed > NoProgressTimeout)
                            {
                                return false;
                            }
                            noProgressStopwatch ??= Stopwatch.StartNew();
                        }

                        // If we haven't printed an error yet (unlikely), inform the user we're waiting.
                        if (!printedPreviousGenerationStillDeploying)
                        {
                            printedPreviousGenerationStillDeploying = true;
                            Console.WriteLine($"Waiting for the deployment '{deploymentName}' generation '{deployment.Status.ObservedGeneration}' to complete...");
                        }

                        previousGenerationProgressCondition = progressCondition;
                        previousGenerationReplicaFailureCondition = replicaFailureCondition;
                    }
                }
                else
                {
                    // Reset state that watches the generation.
                    generationStopwatch = null;
                    noProgressStopwatch = null;
                    printedPreviousGenerationStillDeploying = false;
                    previousGenerationProgressCondition = null;
                    previousGenerationReplicaFailureCondition = null;

                    DeploymentCondition2? progressCondition = deployment.Status.Conditions?.FirstOrDefault(c => c.Type == "Progressing");
                    DeploymentCondition2? replicaFailureCondition = deployment.Status.Conditions?.FirstOrDefault(c => c.Type == "ReplicaFailure");

                    // Check for progress.
                    if (progressCondition?.Status == "True")
                    {
                        if (progressCondition.Reason == "NewReplicaSetAvailable")
                        {
                            // Completed successfully.

                            int availablePods = deployment.Status.AvailableReplicas ?? 0;
                            string availablePodsDescription = availablePods switch
                                    {
                                        1 => "There is 1 available pod.",
                                        _ => $"There are {availablePods} available pods."
                                    };

                            Console.MarkupLine($"The deployment finished successfully. {availablePodsDescription}");

                            return true;
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
        bool mask = false;
        const string Masked = "<<MASKED>>";
        while ((line = await ReadLineAsync(reader, cancellationToken)) != null)
        {
            // Omit empty lines so the build log appears contiguous in the output. 
            if (!line.AsSpan().Trim(' ').IsEmpty)
            {
                if (line.StartsWith("STEP ", StringComparison.InvariantCultureIgnoreCase) &&
                   line.IndexOf(": ") is int colonIndex &&
                   colonIndex != -1)
                {
                    string command = line.Substring(colonIndex + 2);
                    if (command.StartsWith("ENV"))
                    {
                        // Mask ENV section because it may contain passwords or other sensitive information.
                        Console.WriteLine(line.Substring(0, colonIndex) + $": ENV {Masked}");
                        mask = true;
                    }
                    else
                    {
                        Console.WriteLine(line);
                        mask = false;
                    }
                }
                else if (mask)
                {
                    Console.WriteLine(Masked);
                }
                else
                {
                    Console.WriteLine(line);
                }
            }
        }

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

    private async Task<Build> StartBuildAsync(IOpenShiftClient client,
                                              string binaryBuildConfigName,
                                              string contextDir,
                                              string projectFile,
                                              Dictionary<string, string> environment,
                                              CancellationToken cancellationToken)
    {
        Dictionary<string, string> buildEnvironment = new(environment);
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

    private async Task UpdateBuildResourcesAsync(
                                            IOpenShiftClient client,
                                            ComponentResources current,
                                            string name,
                                            string binaryBuildConfigName,
                                            string appImageStreamTagName,
                                            string runtime, string runtimeVersion,
                                            string partOf,
                                            CancellationToken cancellationToken)
    {
        Dictionary<string, string> componentLabels = GetComponentLabels(partOf, name);
        Dictionary<string, string> runtimeLabels = GetRuntimeLabels(runtime, runtimeVersion);

        BuildConfig binaryBuildConfig = await ApplyBinaryBuildConfig(
                                            client,
                                            binaryBuildConfigName,
                                            current.BinaryBuildConfig,
                                            appImageStreamTagName,
                                            s2iImageStreamTag: $"{GetS2iImageStreamName(runtime)}:{runtimeVersion}",
                                            Merge(componentLabels, runtimeLabels),
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
    }

    private async Task<Route?> UpdateResourcesAsync(IOpenShiftClient client,
                                            ComponentResources current,
                                            string name,
                                            string runtime, string runtimeVersion,
                                            string? gitUri, string? gitRef,
                                            string? appImage, string? appImageStreamTagName,
                                            string partOf, bool expose,
                                            global::ContainerPort[] ports, global::ContainerPort? exposedPort,
                                            PersistentStorage[] claims,
                                            ConfMap[] configMaps,
                                            ContainerResources containerResources,
                                            CancellationToken cancellationToken)
    {
        Dictionary<string, string> componentLabels = GetComponentLabels(partOf, name);
        Dictionary<string, string> runtimeLabels = GetRuntimeLabels(runtime, runtimeVersion);
        Dictionary<string, string> selectorLabels = GetSelectorLabels(name);

        Dictionary<string, PersistentVolumeClaim> pvcs = new();
        foreach (var storage in claims)
        {
            string pvcName = GetResourceNameFor(name, storage);
            current.PersistentVolumeClaims.TryGetValue(pvcName, out PersistentVolumeClaim? pvc);
            pvc = await ApplyPersistentVolumeClaim(client,
                        pvcName,
                        pvc,
                        storage,
                        componentLabels,
                        cancellationToken);
            pvcs.Add(pvcName, pvc);
        }

        Dictionary<string, ConfigMap> maps = new();
        foreach (var map in configMaps)
        {
            string mapName = GetResourceNameFor(name, map);
            current.ConfigMaps.TryGetValue(mapName, out ConfigMap? configMap);
            configMap = await ApplyConfigMap(
                    client,
                    mapName,
                    configMap,
                    map,
                    componentLabels,
                    cancellationToken);
            maps.Add(mapName, configMap);
        }

        Deployment? deployment = await ApplyAppDeployment(
                                        client,
                                        name,
                                        current.Deployment,
                                        gitUri, gitRef,
                                        appImage, appImageStreamTagName,
                                        ports,
                                        claims,
                                        configMaps,
                                        Merge(componentLabels, runtimeLabels),
                                        selectorLabels,
                                        containerResources,
                                        cancellationToken
                                    );

        Route? route = null;
        if (expose)
        {
            Debug.Assert(exposedPort is not null);
            route = await ApplyAppRoute(client,
                                name,
                                current.Route,
                                serviceName: name,
                                exposedPort,
                                componentLabels,
                                cancellationToken);
        }

        Service service = await ApplyAppService(client,
                              name,
                              current.Service,
                              ports,
                              componentLabels,
                              selectorLabels,
                              cancellationToken);

        return route;
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