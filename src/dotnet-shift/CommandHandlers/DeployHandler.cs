namespace CommandHandlers;

using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using OpenShift;

sealed partial class DeployHandler
{
    const string InternalRegistryHostName = "image-registry.openshift-image-registry.svc:5000";

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
        public ImageStream? DotnetImageStream { get; init; }
        public ImageStream? DotnetRuntimeImageStream { get; init; }
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
        ComponentResources resources = await GetDeployedResources(client, name, binaryBuildConfigName, storageNames, mapNames, cancellationToken);

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

        string appImageStreamTagName = $"{name}:latest";

        bool enableTrigger = projectInfo.EnableImageStreamTagDeploymentTrigger;

        bool runningInCluster = login.IsPodServiceAccount;

        string? appImage = null;
        if (doBuild)
        {
            Console.WriteLine(); // section "build"
            Console.WriteLine("Updating build resources...");

            // If the trigger was previously enabled and now disabled, we must disable it before doing a build.
            if (!enableTrigger && resources.Deployment is not null && IsImageStreamDeploymentTriggerEnabled(resources.Deployment))
            {
                await DisableImageStreamDeploymentTriggerAsync(client, resources.Deployment, cancellationToken);
            }

            bool useS2iBuild = await UpdateBuildResourcesAsync(client, runningInCluster, resources, name, binaryBuildConfigName, appImageStreamTagName, runtimeVersion, partOf, cancellationToken);
            // TODO: all builds use the SDK, remove the s2i build support.
            useS2iBuild = false;
            if (useS2iBuild)
            {
                appImage = await S2IBuildAsync(projectFile, contextDir, projectInfo, runtimeVersion, binaryBuildConfigName, client, resources, cancellationToken);
            }
            else
            {
                appImage = await SdkImageBuildAsync(client, runningInCluster, projectFile, appImageStreamTagName, runtimeVersion, resources, cancellationToken);
            }
            if (appImage is null)
            {
                return CommandResult.Failure;
            }
        }

        Console.WriteLine(); // section "deployment"
        Console.WriteLine("Updating deployment resources...");
        if (!doBuild)
        {
            // Keep build resources in sync also when we're not doing a build.
            await UpdateBuildResourcesAsync(client, runningInCluster, resources, name, binaryBuildConfigName, appImageStreamTagName, runtimeVersion, partOf, cancellationToken);
        }
        Route? route = await UpdateResourcesAsync(
                                    client, resources, name,
                                    runtimeVersion,
                                    gitUri: gitInfo?.RemoteUrl, gitRef: gitInfo?.RemoteBranch,
                                    appImage, appImageStreamTagName,
                                    partOf, expose,
                                    projectInfo.DeploymentStrategy,
                                    projectInfo.DeploymentEnvironmentVariables,
                                    projectInfo.ContainerPorts, projectInfo.ExposedPort,
                                    projectInfo.VolumeClaims, projectInfo.ConfigMaps,
                                    projectInfo.ContainerLimits,
                                    projectInfo.LivenessProbe, projectInfo.ReadinessProbe, projectInfo.StartupProbe,
                                    enableTrigger,
                                    cancellationToken);

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

    private async Task<string?> SdkImageBuildAsync(IOpenShiftClient client, bool runningInCluster, string projectFile, string appImageStreamTagName, string runtimeVersion, ComponentResources resources, CancellationToken cancellationToken)
    {
        // Ensure the runtime base image is defined and resolved.
        ImageStream? s2iImageStream = await ApplyDotnetImageStreamTag(
                                        client,
                                        DotnetRuntimeImageStreamName,
                                        resources.DotnetRuntimeImageStream,
                                        runtimeVersion,
                                        cancellationToken);
        if (!await CheckImageTagAvailable(client, s2iImageStream, runtimeVersion, cancellationToken))
        {
            return null;
        }
        string? publicRepository = s2iImageStream.Status.PublicDockerImageRepository;

        if (!runningInCluster && publicRepository is not null)
        {
            // OpenShift Local registry runs with a certificate that the .NET SDK won't trust.
            // Try reaching the registry over https, and fallback to a helper pod if there's an AuthenticationException (SSL not trusted).
            try
            {
                using var httpClient = new HttpClient();
                await httpClient.GetAsync($"https://{GetRegistryForPublicRepository(publicRepository)}");
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
            {
                // Fall back to the helper pod.
                publicRepository = null;
            }
        }

        bool useHelperPod = !runningInCluster && publicRepository is null;
        await using HelperPod? helperPod = useHelperPod ? await StartHelperPodAsync(client, runtimeVersion, cancellationToken)
                                                        : null;

        string registry;
        string? registryUserName = null;
        string? registryPassword = null;
        if (runningInCluster)
        {
            registry = InternalRegistryHostName;
        }
        else
        {
            if (useHelperPod)
            {
                Debug.Assert(helperPod is not null);
                X509Certificate2Collection serviceCaCerts = await helperPod.GetServiceCaBundleAsync(cancellationToken);

                Uri proxyUrl = await helperPod.RemoteProxyToInternalRegistryAsync(serviceCaCerts, cancellationToken);
                Debug.Assert(proxyUrl.Host == "127.0.0.1");
                // Change the url to 'localhost' because that is where the .NET SDK uses 'http' instead of 'https'.
                registry = $"localhost:{proxyUrl.Port}";
            }
            else
            {
                Debug.Assert(publicRepository is not null);
                registry = GetRegistryForPublicRepository(publicRepository);
            }
            string? dockerConfig = await GetBuilderDockerConfigAsync(client, cancellationToken);
            if (dockerConfig is null)
            {
                Console.WriteErrorLine("Could not find builder credentials for the OpenShift image registry.");
                return null;
            }
            JsonNode json = JsonNode.Parse(dockerConfig)!;
            JsonNode? auth = json[InternalRegistryHostName];
            if (auth is null)
            {
                Console.WriteErrorLine("Could not find credentials for the OpenShift public image registry route.");
                return null;
            }
            registryUserName = auth["username"]?.GetValue<string>();
            registryPassword = auth["password"]?.GetValue<string>();
        }
        string tag = appImageStreamTagName.Substring(appImageStreamTagName.IndexOf(':') + 1);
        string repository = $"{client.Namespace}/{appImageStreamTagName.Substring(0, appImageStreamTagName.Length - tag.Length - 1)}";
        string containerBaseImage = $"{registry}/{client.Namespace}/{DotnetRuntimeImageStreamName}:{runtimeVersion}";

        // We need the digest, which we can obtain using '--getProperty'.
        // Unfortunately, that leaves the user without any output of how the operation is progressing.
        // To have some output, we first publish the project with console output.
        StringBuilder stdout = new();

        Console.WriteLine();
        Console.WriteLine($"Building .NET project '{projectFile}'...");
        int rv = await RunDotnetAsync(
            new[] {
                "publish",
                projectFile
            },
            envvars: null,
            stdout: null, cancellationToken
        );
        if (rv != -0)
        {
            return null;
        }

        Console.WriteLine();
        Console.WriteLine($"Publishing application image to image registry...");
        Dictionary<string, string>? envvars = null;
        if (registryUserName is not null && registryPassword is not null)
        {
            envvars = new()
            {
                { "SDK_CONTAINER_REGISTRY_UNAME", registryUserName },
                { "SDK_CONTAINER_REGISTRY_PWORD", registryPassword }
            };
        }
        rv = await RunDotnetAsync(
            new[] {
                "publish",
                "/p:PublishProfile=DefaultContainer",
                $"/p:ContainerRegistry={registry}",
                $"/p:ContainerRepository={repository}",
                $"/p:ContainerImageTag={tag}",
                $"/p:ContainerBaseImage={containerBaseImage}",
                "--getProperty:GeneratedContainerDigest",
                projectFile
            },
            envvars,
            stdout, cancellationToken
        );

        if (rv != -0)
        {
            return null;
        }

        string image = $"{InternalRegistryHostName}/{repository}@{stdout.ToString().Trim()}";
        Console.WriteLine($"Succesfully pushed image '{image}'");

        return image;

        static string GetRegistryForPublicRepository(string publicRepository)
            => publicRepository.Substring(0, publicRepository.IndexOf('/'));
    }

    private async Task<string?> GetBuilderDockerConfigAsync(IOpenShiftClient client, CancellationToken cancellationToken)
    {
        SecretList secrets = await client.ListSecretsAsync(labelSelector: null, fieldSelector: "type=kubernetes.io/dockercfg", cancellationToken);
        foreach (var secret in secrets.Items)
        {
            if (secret.Metadata.Name.StartsWith("builder-dockercfg-") &&
                secret.Metadata.Annotations?.TryGetValue("kubernetes.io/service-account.name", out string? sa) == true &&
                sa == "builder")
            {
                return Encoding.UTF8.GetString(secret.Data[".dockercfg"]);
            }
        }
        return null;
    }

    private async Task<int> RunDotnetAsync(IEnumerable<string> arguments, IDictionary<string, string>? envvars, StringBuilder? stdout, CancellationToken cancellationToken)
    {
        Process process = new();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        foreach (var envvar in new[] {
            "MSBuildExtensionsPath",
            "MSBuildSDKsPath",
            "MSBUILD_EXE_PATH",
            "MSBuildLoadMicrosoftTargetsReadOnly",
            "DOTNET_HOST_PATH"
        })
        {
            process.StartInfo.EnvironmentVariables.Remove(envvar);
        }

        if (envvars is not null)
        {
            foreach (var envvar in envvars)
            {
                process.StartInfo.EnvironmentVariables[envvar.Key] = envvar.Value;
            }
        }

        process.Start();

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data is not null)
            {
                Console.WriteLine(e.Data);
            }
        };

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data is not null)
            {
                if (stdout is null)
                {
                    Console.WriteLine(e.Data);
                }
                else
                {
                    stdout?.AppendLine(e.Data);
                }
            }
        };

        process.StandardInput.Close();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return process.ExitCode;
    }

    private async Task<string?> S2IBuildAsync(string projectFile, string contextDir, ProjectInformation projectInfo, string runtimeVersion, string binaryBuildConfigName, IOpenShiftClient client, ComponentResources resources, CancellationToken cancellationToken)
    {
        // Ensure the s2i image for the .NET version is defined.
        ImageStream? s2iImageStream = await ApplyDotnetImageStreamTag(
                                        client,
                                        DotnetImageStreamName,
                                        resources.DotnetImageStream,
                                        runtimeVersion,
                                        cancellationToken);

        // Ensure the s2i image is resolved.
        if (!await CheckImageTagAvailable(client, s2iImageStream, runtimeVersion, cancellationToken))
        {
            return null;
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
            return null;
        }
        Debug.Assert(build.IsBuildFinished());

        // Report build fail/success.
        if (!CheckBuildNotFailed(build, out string? appImage))
        {
            return null;
        }
        Debug.Assert(appImage is not null);
        return appImage;
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
        CancellationToken cancellationToken)
    {
        Deployment? deployment = await client.GetDeploymentAsync(name, cancellationToken);
        BuildConfig? binaryBuildConfig = binaryBuildConfigName is null ? null : await client.GetBuildConfigAsync(binaryBuildConfigName, cancellationToken);
        ConfigMap? configMap = await client.GetConfigMapAsync(name, cancellationToken);
        ImageStream? imageStream = await client.GetImageStreamAsync(name, cancellationToken);
        Route? route = await client.GetRouteAsync(name, cancellationToken);
        Service? service = await client.GetServiceAsync(name, cancellationToken);
        ImageStream? dotnetImageStream = await client.GetImageStreamAsync(DotnetImageStreamName, cancellationToken);
        ImageStream? dotnetRuntimeImageStream = await client.GetImageStreamAsync(DotnetRuntimeImageStreamName, cancellationToken);
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
            DotnetImageStream = dotnetImageStream,
            DotnetRuntimeImageStream = dotnetRuntimeImageStream,
            PersistentVolumeClaims = pvcs,
        };
    }

    private static string GetResourceNameFor(string componentName, PersistentStorage storage)
        => $"{componentName}-{storage.Name}";

    private static string GetResourceNameFor(string componentName, ConfMap confMap)
        => $"{componentName}-{confMap.Name}";

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

    private async Task<bool> UpdateBuildResourcesAsync(
                                            IOpenShiftClient client,
                                            bool runningInCluster,
                                            ComponentResources current,
                                            string name,
                                            string binaryBuildConfigName,
                                            string appImageStreamTagName,
                                            string runtimeVersion,
                                            string partOf,
                                            CancellationToken cancellationToken)
    {
        Dictionary<string, string> componentLabels = GetComponentLabels(partOf, name);
        Dictionary<string, string> runtimeLabels = GetRuntimeLabels(runtimeVersion);

        ImageStream imageStream = await ApplyAppImageStream(client,
                                  name,
                                  current.ImageStream,
                                  Merge(componentLabels, runtimeLabels),
                                  cancellationToken);

        bool useSDKBuild = runningInCluster || !string.IsNullOrEmpty(imageStream.Status?.PublicDockerImageRepository);
        bool useS2iBuild = !useSDKBuild;

        if (useS2iBuild || current.BinaryBuildConfig is not null)
        {
            await ApplyBinaryBuildConfig(
                    client,
                    binaryBuildConfigName,
                    current.BinaryBuildConfig,
                    appImageStreamTagName,
                    s2iImageStreamTag: $"{DotnetImageStreamName}:{runtimeVersion}",
                    Merge(componentLabels, runtimeLabels),
                    cancellationToken);
        }

        return useS2iBuild;
    }

    private async Task<Route?> UpdateResourcesAsync(IOpenShiftClient client,
                                            ComponentResources current,
                                            string name,
                                            string runtimeVersion,
                                            string? gitUri, string? gitRef,
                                            string? appImage, string appImageStreamTagName,
                                            string partOf, bool expose,
                                            global::DeploymentStrategy? deploymentStrategy,
                                            Dictionary<string, string> deploymentEnvvars,
                                            global::ContainerPort[] ports, global::ContainerPort? exposedPort,
                                            PersistentStorage[] claims,
                                            ConfMap[] configMaps,
                                            ContainerResources containerResources,
                                            HttpGetProbe? livenessProbe, HttpGetProbe? readinessProbe, HttpGetProbe? startupProbe,
                                            bool enableTrigger,
                                            CancellationToken cancellationToken)
    {
        Dictionary<string, string> componentLabels = GetComponentLabels(partOf, name);
        Dictionary<string, string> runtimeLabels = GetRuntimeLabels(runtimeVersion);
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
                                        deploymentStrategy,
                                        deploymentEnvvars,
                                        ports,
                                        claims,
                                        configMaps,
                                        Merge(componentLabels, runtimeLabels),
                                        selectorLabels,
                                        containerResources,
                                        enableTrigger,
                                        livenessProbe, readinessProbe, startupProbe,
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

    private static Dictionary<string, string> GetRuntimeLabels(string runtimeVersion)
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