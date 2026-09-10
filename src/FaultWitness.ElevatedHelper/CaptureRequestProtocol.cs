using System.Text.Json;
using System.Text.Json.Serialization;
using FaultWitness.Core;

namespace FaultWitness.ElevatedHelper;

public static class CaptureRequestProtocol
{
    public const int MaxArgumentLength = 12000;
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] Fields = ["SchemaVersion", "ActionId", "Operation", "TargetExecutable", "CreatedUtc", "ExpectedState", "DesiredState"];
    public static CaptureRequest Parse(string encoded)
    {
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxArgumentLength) throw new JsonException();
        if (encoded.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '+' and not '/' and not '=')) throw new JsonException();
        byte[] json; try { json = Convert.FromBase64String(encoded); } catch (FormatException) { throw new JsonException(); }
        if (json.Length > 9000) throw new JsonException();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        ValidateObject(doc.RootElement, Fields);
        var request = JsonSerializer.Deserialize<CaptureRequest>(json, Options) ?? throw new JsonException();
        if (request.SchemaVersion != FaultWitness.Platform.Windows.CaptureHelperCompatibility.SchemaVersion || request.ActionId == Guid.Empty || !Enum.IsDefined(request.Operation) || !CrashCapturePolicy.IsValidExecutable(request.TargetExecutable) ||
            request.CreatedUtc < DateTimeOffset.UtcNow.AddMinutes(-5) || request.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            !CrashCapturePolicy.IsSupportedState(request.ExpectedState) || !CrashCapturePolicy.IsSupportedState(request.DesiredState)) throw new JsonException();
        var validOperation = request.Operation switch
        {
            CaptureOperation.ConfigureApplicationCrashDump => request.DesiredState == CrashCapturePolicy.Desired(request.ExpectedState),
            CaptureOperation.RestoreApplicationCrashDumpConfiguration => request.ExpectedState == CrashCapturePolicy.Desired(request.ExpectedState) && request.DesiredState.OtherValuesFingerprint == request.ExpectedState.OtherValuesFingerprint,
            _ => false
        };
        if (!validOperation) throw new JsonException();
        return request;
    }
    public static bool TryParse(string encoded, out CaptureRequest? request)
    { try { request = Parse(encoded); return true; } catch { request = null; return false; } }
    private static void ValidateObject(JsonElement element, IEnumerable<string> names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException();
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject())
        {
            if (!set.Remove(p.Name)) throw new JsonException();
            if (p.Name is "ExpectedState" or "DesiredState") ValidateObject(p.Value, ["KeyExists", "DumpType", "DumpCount", "DumpFolder", "OtherValuesFingerprint", "Supported"]);
        }
        if (set.Count != 0) throw new JsonException();
    }
}
