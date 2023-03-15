namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private const string DotnetImageStreamName = "dotnet";

    private async Task ApplyDotnetImageStreamTag(
        IOpenShiftClient client,
        ImageStream? current,
        string dotnetVersion,
        CancellationToken cancellationToken)
    {
        string s2iImage = GetS2iImage(dotnetVersion);
        ImageStream imageStream = CreateDotnetImageStream(current, dotnetVersion, s2iImage);

        if (current is null)
        {
            Console.Write($"Adding dotnet image for .NET '{dotnetVersion}.'");
            await client.CreateImageStreamAsync(imageStream, cancellationToken);
        }
        else
        {
            if (current.Spec.Tags.Any(t => t.Name == dotnetVersion))
            {
                return;
            }

            Console.Write($"Adding dotnet image for .NET '{dotnetVersion}.'");
            await client.PatchImageStreamAsync(imageStream, cancellationToken);
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

    private static ImageStream CreateDotnetImageStream(
        ImageStream? current,
        string dotnetVersion,
        string s2iImage)
    {
        return new ImageStream
        {
            ApiVersion = "image.openshift.io/v1",
            Kind = "ImageStream",
            Metadata = new()
            {
                Name = DotnetImageStreamName,
                Annotations = new Dictionary<string, string>()
                {
                    { "openshift.io/display-name", ".NET" },
                    { "openshift.io/provider-display-name", "Red Hat" }
                }
            },
            Spec = new()
            {
                Tags = new()
                {
                    new()
                    {
                        Name = dotnetVersion,
                        Annotations = new Dictionary<string, string>()
                        {
                            { "openshift.io/display-name", $".NET {dotnetVersion}" },
                        },
                        ReferencePolicy = new()
                        {
                            Type = "Local"
                        },
                        From = new()
                        {
                            Kind = "DockerImage",
                            Name = s2iImage
                        },
                        // Poll the s2i image registry for updates.
                        ImportPolicy = new()
                        {
                            Scheduled = true
                        }
                    }
                }
            }
        };
    }
}