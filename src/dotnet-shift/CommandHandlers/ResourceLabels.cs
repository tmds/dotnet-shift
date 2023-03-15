namespace CommandHandlers;

static class ResourceLabels
{
    public const string PartOf = "app.kubernetes.io/part-of";
    public const string Name = "app.kubernetes.io/name";
    public const string Runtime = "app.openshift.io/runtime";
    public const string ManagedBy = "app.kubernetes.io/managed-by";
    public const string Instance = "app.kubernetes.io/instance";
    public const string Component = "app.kubernetes.io/component";
}