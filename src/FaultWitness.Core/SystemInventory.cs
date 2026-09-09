namespace FaultWitness.Core;

public enum InventoryAvailability { Available, Unavailable, AccessDenied, NotSupported }

public sealed record InventoryField(string Key, string? Value, InventoryAvailability Availability = InventoryAvailability.Available);

public sealed record InventoryDevice(string Id, IReadOnlyList<InventoryField> Fields);

public sealed record InventoryGroup(string Key, IReadOnlyList<InventoryDevice> Devices);

public sealed record SystemInventorySnapshot(IReadOnlyList<InventoryGroup> Groups)
{
    public IReadOnlyDictionary<string, string> ToSummary()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            var devices = group.Devices ?? [];
            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                foreach (var field in devices[deviceIndex].Fields ?? [])
                {
                    if (field.Availability != InventoryAvailability.Available || string.IsNullOrWhiteSpace(field.Value)) continue;
                    var key = $"{group.Key}.{deviceIndex}.{field.Key}";
                    result[key] = field.Value;
                }
            }
        }
        return result;
    }
}
