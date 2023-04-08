namespace CommandHandlers;

using Newtonsoft.Json;
using OpenShift;

sealed partial class DeployHandler
{
    private const string ContainerName = "app";
    private const string AppConfigMountPath = "/config";

    private async Task<Deployment> ApplyAppDeployment(
        IOpenShiftClient client,
        string name,
        Deployment? current,
        string imageStreamTagName,
        string? gitUri, string? gitRef,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        CancellationToken cancellationToken)
    {
        Deployment deployment = CreateAppDeployment(
            name,
            imageStreamTagName,
            gitUri, gitRef,
            labels,
            selectorLabels);

        if (current is null)
        {
            return await client.CreateDeploymentAsync(deployment, cancellationToken);
        }
        else
        {
            return await client.PatchDeploymentAsync(deployment, cancellationToken);
        }
    }

    private static Deployment CreateAppDeployment(
        string name,
        string imageStreamTagName,
        string? gitUri, string? gitRef,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels)
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
                                }
                            }
                        }
                    }
                }
            }
        };
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