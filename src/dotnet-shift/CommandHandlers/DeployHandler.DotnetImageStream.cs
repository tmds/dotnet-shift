namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private const string DotnetImageStreamName = "dotnet";

    private async Task<ImageStream> ApplyDotnetImageStreamTag(
        IOpenShiftClient client,
        ImageStream? current,
        string dotnetVersion,
        CancellationToken cancellationToken)
    {
        string s2iImage = GetS2iImage(dotnetVersion);
        ImageStream imageStream = CreateDotnetImageStream(dotnetVersion, s2iImage);

        if (current is null)
        {
            return await client.CreateImageStreamAsync(imageStream, cancellationToken);
        }
        else
        {
            return await client.PatchImageStreamAsync(imageStream, cancellationToken);
        }
    }

    private async Task<bool> CheckRuntimeImageAvailableAsync(IOpenShiftClient client, ImageStream imageStream, string runtimeVersion, CancellationToken cancellationToken)
    {
        string runtime = imageStream.Metadata.Name;
        string s2iImageTag = $"{runtime}:{runtimeVersion}";

        bool printedImageNotYetAvailable = false;
        Stopwatch stopwatch = new();
        stopwatch.Start();

        ImageStream? current = imageStream;
        do
        {
            if (!current.Spec.Tags.Any(t => t.Name == runtimeVersion))
            {
                Console.WriteErrorLine($"The image tag '{s2iImageTag}' is missing.");
                return false;
            }

            NamedTagEventList? tagStatus = current.Status.Tags.FirstOrDefault(t => t.Tag == runtimeVersion);
            if (tagStatus is not null)
            {
                if (tagStatus.Items is { Count: > 0 })
                {
                    return true;
                }

                TagEventCondition? importCondition = tagStatus.Conditions.FirstOrDefault(t => t.Type == "ImportSuccess");
                if (importCondition?.Status == "False")
                {
                    Console.WriteErrorLine($"The {runtime} s2i image is not available for '{runtimeVersion}.' The image could not be imported: \"{importCondition.Message}\".");
                    return false;
                }
            }

            current = await client.GetImageStreamAsync(runtime, cancellationToken);
            if (current is null)
            {
                Console.WriteErrorLine($"The image stream '{runtime}' is missing.");
                return false;
            }

            // Print a message if the image doesn't become available after a short time.
            System.TimeSpan elapsed = stopwatch.Elapsed;
            if (!printedImageNotYetAvailable &&
                elapsed > System.TimeSpan.FromSeconds(5))
            {
                printedImageNotYetAvailable = true;
                Console.WriteLine($"Waiting for the {runtime} s2i image for '{runtimeVersion}' to get imported.");
            }

            await Task.Delay(100, cancellationToken);
        } while (true);
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