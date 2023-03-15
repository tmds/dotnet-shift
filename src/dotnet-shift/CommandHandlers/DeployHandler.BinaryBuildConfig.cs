namespace CommandHandlers;

using OpenShift;

sealed partial class DeployHandler
{
    private async Task ApplyBinaryBuildConfig(
        IOpenShiftClient client,
        string name,
        BuildConfig? current,
        string appImageStreamTag,
        string s2iImageStreamTag,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        BuildConfig buildConfig = CreateBinaryBuildConfig(
                name,
                current,
                appImageStreamTag,
                s2iImageStreamTag,
                labels);

        if (current is null)
        {
            await client.CreateBuildConfigAsync(buildConfig, cancellationToken);
        }
        else
        {
            await client.PatchBuildConfigAsync(buildConfig, cancellationToken);
        }
    }

    private static BuildConfig CreateBinaryBuildConfig(
        string name,
        BuildConfig? current,
        string appImageStreamTag,
        string s2iImageStreamTag,
        Dictionary<string, string> labels)
    {
        return new BuildConfig
        {
            ApiVersion = "build.openshift.io/v1",
            Kind = "BuildConfig",
            Metadata = new()
            {
                Name = name,
                Labels = labels
            },
            Spec = new()
            {
                FailedBuildsHistoryLimit = 5,
                SuccessfulBuildsHistoryLimit = 5,
                Output = new()
                {
                    To = new()
                    {
                        Kind = "ImageStreamTag",
                        Name = appImageStreamTag
                    }
                },
                Source = new()
                {
                    Type = "Binary"
                },
                Strategy = new()
                {
                    Type = "Source",
                    SourceStrategy = new()
                    {
                        From = new()
                        {
                            Kind = "ImageStreamTag",
                            Name = s2iImageStreamTag
                        }
                    }
                }
            }
        };
    }
}