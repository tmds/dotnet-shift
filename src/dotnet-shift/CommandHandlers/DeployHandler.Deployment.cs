namespace CommandHandlers;

using System;
using Newtonsoft.Json;
using OpenShift;

sealed partial class DeployHandler
{
    private const string ContainerName = "app";
    private const string AppConfigMountPath = "/config";

    private async Task<Deployment> ApplyAppDeployment(
        IOpenShiftClient client,
        string name,
        Deployment? previous,
        string imageStreamTagName,
        string? gitUri, string? gitRef,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        ContainerResources containerLimits,
        CancellationToken cancellationToken)
    {
        Deployment deployment = CreateAppDeployment(
            name,
            imageStreamTagName,
            gitUri, gitRef,
            labels,
            selectorLabels,
            containerLimits);

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
                                                       },
                                                       cancellationToken);
        }
    }

    private static Container? FindAppContainer(List<Container> containers)
        => containers.FirstOrDefault(c => c.Name == ContainerName);

    private static Deployment CreateAppDeployment(
        string name,
        string imageStreamTagName,
        string? gitUri, string? gitRef,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        ContainerResources containerLimits)
    {
        Dictionary<string, string> annotations = GetAppDeploymentAnnotations(imageStreamTagName, gitUri, gitRef);

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
                        Volumes = new()
                        {
                            // Volume for application configuration.
                            new()
                            {
                                Name = "config-volume",
                                ConfigMap = new()
                                {
                                    Name = name
                                }
                            }
                        },
                        Containers = new()
                        {
                            new()
                            {
                                Name = ContainerName,
                                Image = imageStreamTagName,
                                Ports = new()
                                {
                                    new()
                                    {
                                        ContainerPort1 = 8080,
                                        Name = "http",
                                        Protocol = ContainerPortProtocol.TCP
                                    }
                                },
                                VolumeMounts = new()
                                {
                                    new()
                                    {
                                        Name = "config-volume",
                                        MountPath = AppConfigMountPath
                                    }
                                },
                                Resources = CreateResourceRequirements(containerLimits)
                            }
                        }
                    }
                }
            }
        };

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
    }

    private static Dictionary<string, string> GetAppDeploymentAnnotations(string imageStreamTagName, string? gitUri, string? gitRef)
    {
        Dictionary<string, string> annotations = new()
        {
            { Annotations.OpenShiftTriggers, Serialize(new List<DeploymentTrigger>()
                {
                        new()
                        {
                            From = new()
                            {
                                Kind = "ImageStreamTag",
                                Name = imageStreamTagName
                            },
                            FieldPath = $"spec.template.spec.containers[?(@.name==\"{ContainerName}\")].image",
                            Pause = "false"
                        }
                })
            }
        };
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