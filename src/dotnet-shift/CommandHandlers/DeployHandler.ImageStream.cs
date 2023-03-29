namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task<ImageStream> ApplyAppImageStream(
        IOpenShiftClient client,
        string name,
        ImageStream? current,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        ImageStream imageStream = CreateAppImageStream(
                name,
                current,
                labels);

        if (current is null)
        {
            return await client.CreateImageStreamAsync(imageStream, cancellationToken);
        }
        else
        {
            return await client.PatchImageStreamAsync(imageStream, cancellationToken);
        }
    }

    private static ImageStream CreateAppImageStream(
        string name,
        ImageStream? current,
        Dictionary<string, string> labels)
    {
        return new ImageStream
        {
            ApiVersion = "image.openshift.io/v1",
            Kind = "ImageStream",
            Metadata = new()
            {
                Name = name,
                Labels = labels
            }
        };
    }
}