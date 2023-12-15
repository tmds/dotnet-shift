namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private const string DotnetImageStreamName = "dotnet";
    private const string DotnetRuntimeImageStreamName = "dotnet-runtime";

    private async Task<ImageStream> ApplyDotnetImageStreamTag(
        IOpenShiftClient client,
        string imageStreamName,
        ImageStream? previous,
        string version,
        CancellationToken cancellationToken)
    {
        Debug.Assert(previous is null || previous.Metadata.Name == imageStreamName);

        // If a tag is defined for the version already, no-op.
        if (previous is not null &&
            previous.Spec.Tags.Any(t => t.Name == version))
        {
            return previous;
        }

        string image = DetermineDotnetImageStreamImage(imageStreamName, version);
        ImageStream imageStream = CreateDotnetImageStream(imageStreamName, version, image);

        if (previous is null)
        {
            return await client.CreateImageStreamAsync(imageStream, cancellationToken);
        }
        else
        {
            // Patch to preserve other .NET versions.
            return await client.PatchImageStreamAsync(imageStream, cancellationToken);
        }
    }

    private async Task<bool> CheckImageTagAvailable(IOpenShiftClient client, ImageStream imageStream, string tag, CancellationToken cancellationToken)
    {
        string image = imageStream.Metadata.Name;
        string imageWithTag = $"{image}:{tag}";

        bool printedImageNotYetAvailable = false;
        Stopwatch stopwatch = new();
        stopwatch.Start();

        ImageStream? current = imageStream;
        do
        {
            if (!current.Spec.Tags.Any(t => t.Name == tag))
            {
                Console.WriteErrorLine($"The image tag '{imageWithTag}' is missing.");
                return false;
            }

            NamedTagEventList? tagStatus = current.Status.Tags?.FirstOrDefault(t => t.Tag == tag);
            if (tagStatus is not null)
            {
                if (tagStatus.Items is { Count: > 0 })
                {
                    return true;
                }

                TagEventCondition? importCondition = tagStatus.Conditions?.FirstOrDefault(t => t.Type == "ImportSuccess");
                if (importCondition?.Status == "False")
                {
                    Console.WriteErrorLine($"The {image} s2i image is not available for '{tag}.' The image could not be imported: \"{importCondition.Message}\".");
                    return false;
                }
            }

            current = await client.GetImageStreamAsync(image, cancellationToken);
            if (current is null)
            {
                Console.WriteErrorLine($"The image stream '{image}' is missing.");
                return false;
            }

            // Print a message if the image doesn't become available after a short time.
            TimeSpan elapsed = stopwatch.Elapsed;
            if (!printedImageNotYetAvailable &&
                elapsed > ShortFeedbackTimeout)
            {
                printedImageNotYetAvailable = true;
                Console.WriteLine($"Waiting for the {image} s2i image for '{tag}' to get imported.");
            }

            await Task.Delay(100, cancellationToken);
        } while (true);
    }

    private static string DetermineDotnetImageStreamImage(string imageStreamName, string version)
    {
        string versionNoDot = version.Replace(".", "");

        string suffix = imageStreamName == DotnetRuntimeImageStreamName ? "-runtime" : "";

        return $"registry.access.redhat.com/{DotNetVersionToRedHatBaseImage(version)}/dotnet-{versionNoDot}{suffix}:latest";

        static string DotNetVersionToRedHatBaseImage(string version) => version switch
        {
            _ => "ubi8"
        };
    }

    private static ImageStream CreateDotnetImageStream(
        string imageStreamName,
        string dotnetVersion,
        string containerImage)
    {
        string displayName = imageStreamName == DotnetRuntimeImageStreamName ? ".NET Runtime" : ".NET";
        return new ImageStream
        {
            ApiVersion = "image.openshift.io/v1",
            Kind = "ImageStream",
            Metadata = new()
            {
                Name = imageStreamName,
                Annotations = new Dictionary<string, string>()
                {
                    { "openshift.io/display-name", displayName },
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
                            { "openshift.io/display-name", $"{displayName} {dotnetVersion}" },
                        },
                        ReferencePolicy = new()
                        {
                            Type = "Local"
                        },
                        From = new()
                        {
                            Kind = "DockerImage",
                            Name = containerImage
                        }
                    }
                }
            }
        };
    }
}