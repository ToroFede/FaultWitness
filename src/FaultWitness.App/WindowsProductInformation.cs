namespace FaultWitness.App;

public sealed record WindowsProductInformation(string? Caption, string? DisplayVersion, string? BuildNumber, string? NtVersion)
{
    // Legacy presentation adapter; Windows collection is owned by the platform layer.
    public IReadOnlyDictionary<string, string> ApplyTo(IReadOnlyDictionary<string, string> inventory)
    {
        var result = inventory.Where(pair => pair.Key != "OperatingSystem").ToDictionary(pair => pair.Key, pair => pair.Value);
        if (inventory.TryGetValue("OperatingSystem", out var raw)) result["NtVersion"] = raw;
        if (!string.IsNullOrWhiteSpace(Caption)) result["OperatingSystem"] = Caption;
        if (!string.IsNullOrWhiteSpace(DisplayVersion)) result["DisplayVersion"] = DisplayVersion;
        if (!string.IsNullOrWhiteSpace(BuildNumber)) result["BuildNumber"] = BuildNumber;
        if (!string.IsNullOrWhiteSpace(NtVersion)) result["NtVersion"] = NtVersion;
        return result;
    }
}
