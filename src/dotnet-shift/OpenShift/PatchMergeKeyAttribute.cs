namespace OpenShift;

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple=false)]
sealed class PatchMergeKeyAttribute : Attribute
{
    public string MergeKey { get; }

    public PatchMergeKeyAttribute(string mergeKey)
    {
        MergeKey = mergeKey;
    }
}