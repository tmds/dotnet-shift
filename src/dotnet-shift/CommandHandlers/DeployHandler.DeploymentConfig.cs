namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private const string ContainerName = "app";
    private const string AppConfigMountPath = "/config";

    private async Task ApplyAppDeploymentConfig(
        IOpenShiftClient client,
        string name,
        DeploymentConfig? current,
        string imageStreamTagName,
        Dictionary<string, string> annotations,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels,
        CancellationToken cancellationToken)
    {
        DeploymentConfig deploymentConfig = CreateAppDeploymentConfig(
            name,
            current,
            imageStreamTagName,
            annotations,
            labels,
            selectorLabels);

        if (current is null)
        {
            await client.CreateDeploymentConfigAsync(deploymentConfig, cancellationToken);
        }
        else
        {
            await client.PatchDeploymentConfigAsync(deploymentConfig, cancellationToken);
        }
    }

    private static DeploymentConfig CreateAppDeploymentConfig(
        string name,
        DeploymentConfig? current,
        string imageStreamTagName,
        Dictionary<string, string> annotations,
        Dictionary<string, string> labels,
        Dictionary<string, string> selectorLabels)
    {
        return new DeploymentConfig()
        {
            ApiVersion = "apps.openshift.io/v1",
            Kind = "DeploymentConfig",
            Metadata = new()
            {
                Name = name,
                Annotations = annotations,
                Labels = labels
            },
            Spec = new()
            {
                Replicas = current?.Spec?.Replicas ?? 1,
                Selector = selectorLabels,
                Triggers = new()
                {
                    // Trigger a deployment when the configuration changes.
                    new()
                    {
                        Type = "ConfigChange"
                    },
                    // Trigger a deployment when the application image changes.
                    new()
                    {
                        Type = "ImageChange",
                        ImageChangeParams = new()
                        {
                            Automatic = true,
                            ContainerNames = new()
                            {
                                ContainerName
                            },
                            From = new()
                            {
                                Kind = "ImageStreamTag",
                                Name = imageStreamTagName
                            }
                        }
                    }
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
                                SecurityContext = new()
                                {
                                    Privileged = false
                                },
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
}