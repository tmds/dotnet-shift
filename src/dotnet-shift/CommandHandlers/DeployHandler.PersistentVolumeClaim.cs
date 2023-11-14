namespace CommandHandlers;

using System.Collections.Generic;
using OpenShift;

sealed partial class DeployHandler
{
    private async Task<PersistentVolumeClaim> ApplyPersistentVolumeClaim(
        IOpenShiftClient client,
        string name,
        PersistentVolumeClaim? previous,
        PersistentStorage storage,
        Dictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        PersistentVolumeClaim pvc = CreatePersistentVolumeClaim(
                name,
                storage,
                previous,
                labels);

        if (previous is null)
        {
            return await client.CreatePersistentVolumeClaimAsync(pvc, cancellationToken);
        }
        else
        {
            return await client.PatchPersistentVolumeClaimAsync(pvc, cancellationToken);
        }
    }

    private static PersistentVolumeClaim CreatePersistentVolumeClaim(
        string name,
        PersistentStorage storage,
        PersistentVolumeClaim? previous,
        Dictionary<string, string> labels)
    {
        // PersistentVolumeClaims can't be made smaller once they were created.
        ResourceQuantity size = storage.Size;
        if (previous is not null &&
            previous.Spec.Resources.Requests.TryGetValue("storage", out string? previousStorage) &&
            ResourceQuantity.TryParse(previousStorage, out ResourceQuantity? previousSize))
        {
            size = ResourceQuantity.Max(size, previousSize);
        }

        return new PersistentVolumeClaim
        {
            ApiVersion = "v1",
            Kind = "PersistentVolumeClaim",
            Metadata = new()
            {
                Name = name,
                Labels = labels,
            },
            Spec = new()
            {
                AccessModes = new() { storage.Access },
                Resources = new()
                {
                    Requests = new Dictionary<string, string>()
                    {
                        { "storage", size.ToString() }
                    },
                    Limits = CreateLimitsDictionary(storage)
                },
                StorageClassName = storage.StorageClass
            }
        };

        static IDictionary<string, string>? CreateLimitsDictionary(PersistentStorage storage)
        {
            if (storage.Limit is null)
            {
                return null;
            }
            return new Dictionary<string, string>()
            {
                { "storage", storage.Size.ToString() }
            };
        }
    }
}