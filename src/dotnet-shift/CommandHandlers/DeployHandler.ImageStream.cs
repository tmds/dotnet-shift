namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task<ImageStream> ApplyAppImageStream(
        IOpenShiftClient client,
        string name,
        ImageStream? previous,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        ImageStream imageStream = CreateAppImageStream(
                name,
                labels);

        if (previous is null)
        {
            return await client.CreateImageStreamAsync(imageStream, cancellationToken);
        }
        else
        {
            return await client.ReplaceImageStreamAsync(previous, imageStream, update: null, cancellationToken);
        }
    }

    private static ImageStream CreateAppImageStream(
        string name,
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