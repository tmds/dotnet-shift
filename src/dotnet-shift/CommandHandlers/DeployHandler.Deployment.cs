namespace CommandHandlers;

using System;
using Newtonsoft.Json;
using OpenShift;

sealed partial class DeployHandler
{
    // This is used to find back the application container on existing deployments. Do not change!
    private const string ContainerName = "app";
    private const string TriggerFieldPath = $"spec.template.spec.containers[?(@.name==\"{ContainerName}\")].image";

    private async Task<Deployment> ApplyAppDeployment(
        IOpenShiftClient client,
        string name,
        Deployment? previous,
        string? gitUri, string? gitRef,
        string? appImage, string appImageStreamTagName,
        global::ContainerPort[] ports,
        global::DeploymentStrategy? deploymentStrategy,
        PersistentStorage[] claims,
        ConfMap[] configMaps,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        ContainerResources containerLimits,
        bool enableTrigger,
        HttpGetProbe? livenessProbe, HttpGetProbe? readinessProbe, HttpGetProbe? startupProbe,
        CancellationToken cancellationToken)
    {
        Deployment deployment = CreateAppDeployment(
            name,
            gitUri, gitRef,
            appImage,
            appImageStreamTagName,
            ports,
            deploymentStrategy,
            claims,
            configMaps,
            labels,
            selectorLabels,
            containerLimits,
            livenessProbe, readinessProbe, startupProbe,
            enableTrigger);

        if (previous is null)
        {
            return await client.CreateDeploymentAsync(deployment, cancellationToken);
        }
        else
        {
            return await client.ReplaceDeploymentAsync(previous,
                                                       deployment,
                                                       update: (previous, update) =>
                                                       {
                                                           // Preserve the number of deployed pods.
                                                           update.Spec.Replicas = previous.Spec.Replicas;
                                                           // Preserve resources set on the previous deployment.
                                                           FindAppContainer(update.Spec.Template.Spec.Containers)!.Resources ??= FindAppContainer(previous.Spec.Template.Spec.Containers)?.Resources;
                                                           // Preserve image set on the previous deployment.
                                                           FindAppContainer(update.Spec.Template.Spec.Containers)!.Image ??= FindAppContainer(previous.Spec.Template.Spec.Containers)?.Image;
                                                       },
                                                       cancellationToken);
        }
    }

    private static Container? FindAppContainer(List<Container> containers)
        => containers.FirstOrDefault(c => c.Name == ContainerName);

    private bool IsImageStreamDeploymentTriggerEnabled(Deployment deployment)
    {
        List<DeploymentTrigger>? triggers = null;
        if (deployment.Metadata.Annotations.TryGetValue(Annotations.OpenShiftTriggers, out string? triggersAnnotation))
        {
            triggers = JsonConvert.DeserializeObject<List<DeploymentTrigger>>(triggersAnnotation);
        }
        if (triggers is not null)
        {
            foreach (var trigger in triggers)
            {
                if (trigger.FieldPath == TriggerFieldPath && trigger.Paused != "true")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Task DisableImageStreamDeploymentTriggerAsync(IOpenShiftClient client, Deployment deployment, CancellationToken cancellationToken)
    {
        List<DeploymentTrigger>? triggers = null;
        if (deployment.Metadata.Annotations.TryGetValue(Annotations.OpenShiftTriggers, out string? triggersAnnotation))
        {
            triggers = JsonConvert.DeserializeObject<List<DeploymentTrigger>>(triggersAnnotation);
        }
        bool triggerFound = false;
        if (triggers is not null)
        {
            foreach (var trigger in triggers)
            {
                if (trigger.FieldPath == TriggerFieldPath && trigger.Paused != "true")
                {
                    triggerFound = true;
                    trigger.Paused = "true";
                }
            }

            if (triggerFound)
            {
                return client.PatchDeploymentAsync(new Deployment()
                {
                    ApiVersion = "apps/v1",
                    Kind = "Deployment",
                    Metadata = new()
                    {
                        Name = deployment.Metadata.Name,
                        Annotations = new Dictionary<string, string>()
                        {
                            { Annotations.OpenShiftTriggers, Serialize(triggers) }
                        }
                    },
                }, cancellationToken);
            }
        }

        return Task.CompletedTask;
    }

    private static Deployment CreateAppDeployment(
        string name,
        string? gitUri, string? gitRef,
        string? appImage, string appImageStreamTagName,
        global::ContainerPort[] ports,
        global::DeploymentStrategy? deploymentStrategy,
        PersistentStorage[] claims,
        ConfMap[] configMaps,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        ContainerResources containerLimits,
        HttpGetProbe? livenessProbe, HttpGetProbe? readinessProbe, HttpGetProbe? startupProbe,
        bool enableTrigger)
    {
        const string PvcPrefix = "pvc";
        const string ConfigMapPrefix = "cfg";

        Dictionary<string, string> annotations = GetAppDeploymentAnnotations(gitUri, gitRef, appImageStreamTagName, enableTrigger);

        DeploymentStrategy2Type strategy;
        if (deploymentStrategy.HasValue)
        {
            strategy = deploymentStrategy.Value switch
            {
                global::DeploymentStrategy.Recreate => DeploymentStrategy2Type.Recreate,
                global::DeploymentStrategy.RollingUpdate => DeploymentStrategy2Type.RollingUpdate,
                _ => throw new ArgumentOutOfRangeException(deploymentStrategy.ToString())
            };
        }
        else
        {
            // If only a single mount is allowed, the previous pod must be down, so the new pod can attach.
            bool hasReadWriteOnceVolumes = claims.Any(c => c.Access == "ReadWriteOnce" || c.Access == "ReadWriteOncePod");
            strategy = hasReadWriteOnceVolumes ? DeploymentStrategy2Type.Recreate : DeploymentStrategy2Type.RollingUpdate;
        }

        return new Deployment()
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new()
            {
                Name = name,
                Annotations = annotations,
                Labels = labels
            },
            Spec = new()
            {
                Strategy = new()
                {
                    Type = strategy
                },
                Replicas = null, // Defaults to '1'.
                Selector = new()
                {
                    MatchLabels = selectorLabels
                },
                Template = new()
                {
                    Metadata = new()
                    {
                        Labels = selectorLabels
                    },
                    Spec = new()
                    {
                        Volumes = CreateVolumes(name, claims, configMaps),
                        Containers = new()
                        {
                            new()
                            {
                                Name = ContainerName,
                                Image = appImage,
                                Ports = CreateContainerPorts(ports),
                                VolumeMounts = CreateVolumeMounts(claims, configMaps),
                                Resources = CreateResourceRequirements(containerLimits),
                                LivenessProbe = CreateProbe(livenessProbe),
                                ReadinessProbe = CreateProbe(readinessProbe),
                                StartupProbe = CreateProbe(startupProbe),
                            }
                        }
                    }
                }
            }
        };

        static Probe2? CreateProbe(HttpGetProbe? probe)
        {
            if (probe is null)
            {
                return null;
            }

            return new Probe2()
            {
                FailureThreshold = probe.FailureThresholdCount,
                InitialDelaySeconds = probe.InitialDelay,
                PeriodSeconds = probe.Period,
                TimeoutSeconds = probe.Timeout,
                HttpGet = new HTTPGetAction()
                {
                    Port = probe.Port,
                    Path = probe.Path
                }
            };
        }

        static ResourceRequirements? CreateResourceRequirements(ContainerResources containerLimits)
        {
            // note: resources are updated as a whole, not per setting.
            // We also return an object when no resource constraints are set to support clearing resource requirements.
            // Consequently, we don't support preserving constraints set by other means.
            ResourceRequirements requirements = new();
            if (containerLimits.ContainerCpuRequest is not null ||
                containerLimits.ContainerMemoryRequest is not null)
            {
                requirements.Requests = new Dictionary<string, string>();
                if (containerLimits.ContainerMemoryRequest is not null)
                {
                    requirements.Requests.Add("memory", containerLimits.ContainerMemoryRequest.ToString());
                }
                if (containerLimits.ContainerCpuRequest is not null)
                {
                    requirements.Requests.Add("cpu", containerLimits.ContainerCpuRequest.ToString());
                }
            }
            if (containerLimits.ContainerCpuLimit is not null ||
                containerLimits.ContainerMemoryLimit is not null)
            {
                requirements.Limits = new Dictionary<string, string>();
                if (containerLimits.ContainerMemoryLimit is not null)
                {
                    requirements.Limits.Add("memory", containerLimits.ContainerMemoryLimit.ToString());
                }
                if (containerLimits.ContainerCpuLimit is not null)
                {
                    requirements.Limits.Add("cpu", containerLimits.ContainerCpuLimit.ToString());
                }
            }
            return requirements;
        }

        static List<Volume> CreateVolumes(string name, PersistentStorage[] claims, ConfMap[] configMaps)
        {
            List<Volume> volumes = new();

            foreach (var map in configMaps)
            {
                volumes.Add(new()
                {
                    Name = VolumeName(ConfigMapPrefix, map.Name),
                    ConfigMap = new()
                    {
                        Name = GetResourceNameFor(name, map)
                    }
                });
            }

            foreach (var claim in claims)
            {
                volumes.Add(new()
                {
                    Name = VolumeName(PvcPrefix, claim.Name),
                    PersistentVolumeClaim = new()
                    {
                        ClaimName = GetResourceNameFor(name, claim)
                    }
                });
            }

            return volumes;
        }

        static List<VolumeMount> CreateVolumeMounts(PersistentStorage[] claims, ConfMap[] configMaps)
        {
            List<VolumeMount> mounts = new();

            foreach (var map in configMaps)
            {
                mounts.Add(new()
                {
                    Name = VolumeName(ConfigMapPrefix, map.Name),
                    MountPath = map.Path,
                    ReadOnly = map.MountReadOnly
                });
            }

            foreach (var claim in claims)
            {
                mounts.Add(new()
                {
                    Name = VolumeName(PvcPrefix, claim.Name),
                    MountPath = claim.Path,
                    ReadOnly = claim.MountReadOnly
                });
            }

            return mounts;
        }

        static string VolumeName(string prefix, string name) => $"{prefix}-{name}";
    }

    private static List<ContainerPort> CreateContainerPorts(global::ContainerPort[] ports)
    {
        return ports.Select(p => new ContainerPort()
        {
            ContainerPort1 = p.Port,
            Name = p.Name,
            Protocol = p.Type switch
            {
                "tcp" => ContainerPortProtocol.TCP,
                "udp" => ContainerPortProtocol.UDP,
                _ => throw new ArgumentException(p.Type)
            }
        }).ToList();
    }

    private static Dictionary<string, string> GetAppDeploymentAnnotations(string? gitUri, string? gitRef, string? appImageStreamTagName, bool enableTrigger)
    {
        Dictionary<string, string> annotations = new();
        if (appImageStreamTagName is not null)
        {
            // OpenShift Console uses this to group the BuildConfig under the Deployment resources by matching it via an ImageStreamTag.
            annotations[Annotations.OpenShiftTriggers] =
                Serialize(
                    new List<DeploymentTrigger>()
                    {
                            new()
                            {
                                From = new()
                                {
                                    Kind = "ImageStreamTag",
                                    Name = appImageStreamTagName
                                },
                                FieldPath = TriggerFieldPath,
                                // Set the trigger to Pause so changes to the deployment are deployed in sync with the image.
                                Paused = enableTrigger ? "false" : "true"
                            }
                    }
                );
        }
        if (gitUri is not null && gitRef is not null)
        {
            annotations[Annotations.VersionControlRef] = gitRef;
            annotations[Annotations.VersionControlUri] = gitUri;
        }

        return annotations;
    }

    private static string Serialize(List<DeploymentTrigger> deploymentTriggers)
        => JsonConvert.SerializeObject(deploymentTriggers);
}