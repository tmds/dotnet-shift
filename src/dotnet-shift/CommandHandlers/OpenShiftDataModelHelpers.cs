namespace CommandHandlers;

using OpenShift;

static class OpenShiftDataModelHelpers
{
    private static readonly string[] BuildFinishedPhases = new[]
        {
            "Complete",
            "Failed",
            "Error",
            "Cancelled"
        };

    public static string GetRouteUrl(this Route route)
        => $"http://{route.Spec.Host}"; // TODO: detect https routes.

    public static string GetName(this Route resource)
        => resource.Metadata.Name;
    public static string GetName(this BuildConfig resource)
        => resource.Metadata.Name;
    public static string GetName(this DeploymentConfig resource)
        => resource.Metadata.Name;
    public static string GetName(this Deployment resource)
        => resource.Metadata.Name;
    public static string GetName(this Service resource)
        => resource.Metadata.Name;
    public static string GetName(this ConfigMap resource)
        => resource.Metadata.Name;
    public static string GetName(this Pod resource)
        => resource.Metadata.Name;
    public static string GetName(this ImageStream resource)
        => resource.Metadata.Name;

    public static bool IsBuildFinished(this Build build)
        => BuildFinishedPhases.Contains(build.Status.Phase);

    public static bool IsBuildSuccess(this Build build)
        => build.Status.Phase == "Complete";
}