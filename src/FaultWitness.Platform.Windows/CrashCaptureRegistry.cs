using System.Security.Cryptography;
using System.Text;
using FaultWitness.Core;
using Microsoft.Win32;

namespace FaultWitness.Platform.Windows;

public interface ILocalDumpRegistry
{
    LocalDumpState ReadState(string executable);
    void Configure(string executable, LocalDumpState desired);
    void Restore(string executable, LocalDumpState prior);
}

public sealed class WindowsLocalDumpRegistry : ILocalDumpRegistry
{
    private const string Root = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";
    private static RegistryKey OpenBase() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
        Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);

    public CaptureReadResult Read(string executable)
    {
        try { if (!CrashCapturePolicy.IsValidExecutable(executable)) return new(CaptureResultCode.InvalidRequest, null); return new(CaptureResultCode.Success, ReadState(executable)); }
        catch (UnauthorizedAccessException) { return new(CaptureResultCode.AccessDenied, null); }
        catch (PlatformNotSupportedException) { return new(CaptureResultCode.UnsupportedPlatform, null); }
        catch { return new(CaptureResultCode.RegistryUnavailable, null); }
    }

    public LocalDumpState ReadState(string executable)
    {
        if (!CrashCapturePolicy.IsValidExecutable(executable)) throw new ArgumentException("Unsupported capture target.", nameof(executable));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var root = OpenBase(); using var key = root.OpenSubKey(Root + "\\" + executable, false);
        if (key is null) return new(false);
        var values = key.GetValueNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(n => (n, Value: key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames), Kind: key.GetValueKind(n))).ToArray();
        var typeValue = values.FirstOrDefault(x => x.n.Equals("DumpType", StringComparison.OrdinalIgnoreCase));
        var countValue = values.FirstOrDefault(x => x.n.Equals("DumpCount", StringComparison.OrdinalIgnoreCase));
        var folderValue = values.FirstOrDefault(x => x.n.Equals("DumpFolder", StringComparison.OrdinalIgnoreCase));
        int? Number((string n, object? Value, RegistryValueKind Kind) v) => v.Value is null ? null : v.Value is int i && v.Kind == RegistryValueKind.DWord ? i : throw new InvalidDataException();
        var rawFolder = folderValue.Value as string;
        var supportedFolder = folderValue.Value is null || folderValue.Kind == RegistryValueKind.ExpandString && rawFolder is CrashCapturePolicy.Folder or @"%LOCALAPPDATA%\CrashDumps";
        var folder = supportedFolder ? rawFolder : null;
        var subkeys = key.GetSubKeyNames();
        try { return new(true, Number(typeValue), Number(countValue), folder, Fingerprint(values, subkeys), supportedFolder && subkeys.Length == 0 && values.All(v => v.Value is not byte[] b || b.Length <= 4096)); }
        catch (InvalidDataException) { return new(true, null, null, null, Fingerprint(values, key.GetSubKeyNames()), false); }
    }

    public void Configure(string executable, LocalDumpState desired)
    {
        if (!CrashCapturePolicy.IsValidExecutable(executable) || !CrashCapturePolicy.IsSupportedState(desired) || !desired.KeyExists || desired.DumpType != CrashCapturePolicy.DumpType || desired.DumpCount != CrashCapturePolicy.DumpCount || desired.DumpFolder != CrashCapturePolicy.Folder) throw new ArgumentException("Unsupported capture target or state.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var root = OpenBase(); using var key = root.CreateSubKey(Root + "\\" + executable, writable: true) ?? throw new UnauthorizedAccessException();
        key.SetValue("DumpType", desired.DumpType!.Value, RegistryValueKind.DWord);
        key.SetValue("DumpCount", desired.DumpCount!.Value, RegistryValueKind.DWord);
        key.SetValue("DumpFolder", desired.DumpFolder!, RegistryValueKind.ExpandString);
    }

    public void Restore(string executable, LocalDumpState prior)
    {
        if (!CrashCapturePolicy.IsValidExecutable(executable) || !CrashCapturePolicy.IsSupportedState(prior)) throw new ArgumentException("Unsupported capture target or state.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var root = OpenBase(); using var key = root.OpenSubKey(Root + "\\" + executable, writable: true);
        if (key is null) return;
        if (prior.DumpType is int type) key.SetValue("DumpType", type, RegistryValueKind.DWord); else key.DeleteValue("DumpType", false);
        if (prior.DumpCount is int count) key.SetValue("DumpCount", count, RegistryValueKind.DWord); else key.DeleteValue("DumpCount", false);
        if (prior.DumpFolder is string folder) key.SetValue("DumpFolder", folder, RegistryValueKind.ExpandString); else key.DeleteValue("DumpFolder", false);
        if (!prior.KeyExists && key.GetValueNames().Length == 0 && key.GetSubKeyNames().Length == 0)
            using (var parent = root.OpenSubKey(Root, true)) parent?.DeleteSubKey(executable, false);
    }

    private static string Fingerprint(IEnumerable<(string n, object? Value, RegistryValueKind Kind)> values, IEnumerable<string> subkeys)
    {
        var text = string.Join("\n", values.Where(v => !v.n.Equals("DumpType", StringComparison.OrdinalIgnoreCase) && !v.n.Equals("DumpCount", StringComparison.OrdinalIgnoreCase) && !v.n.Equals("DumpFolder", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.n + "|" + v.Kind + "|" + Convert.ToBase64String(TypedBytes(v.Value)))
            .Concat(subkeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => "K|" + x)));
        return text.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    private static byte[] TypedBytes(object? value) => value switch
    {
        null => [], byte[] b => b, string s => Encoding.Unicode.GetBytes(s), int i => BitConverter.GetBytes(i),
        long l => BitConverter.GetBytes(l), uint u => BitConverter.GetBytes(u), ulong ul => BitConverter.GetBytes(ul),
        string[] a => Encoding.Unicode.GetBytes(string.Join("\0", a)), _ => Encoding.UTF8.GetBytes(value.ToString() ?? "")
    };
}

public sealed class LocalDumpCaptureEngine
{
    private readonly ILocalDumpRegistry registry;
    public LocalDumpCaptureEngine(ILocalDumpRegistry registry) => this.registry = registry;
    public CaptureReadResult Read(string executable)
    {
        try { if (!CrashCapturePolicy.IsValidExecutable(executable)) return new(CaptureResultCode.InvalidRequest, null); return new(CaptureResultCode.Success, registry.ReadState(executable)); }
        catch (UnauthorizedAccessException) { return new(CaptureResultCode.AccessDenied, null); }
        catch (PlatformNotSupportedException) { return new(CaptureResultCode.UnsupportedPlatform, null); }
        catch (Exception) { return new(CaptureResultCode.RegistryUnavailable, null); }
    }
    public CaptureResult Execute(CaptureRequest request)
    {
        if (!Validate(request)) return new(CaptureResultCode.InvalidRequest);
        LocalDumpState? observed = null;
        try
        {
            observed = registry.ReadState(request.TargetExecutable);
            if (!CrashCapturePolicy.IsSupportedState(observed)) return new(CaptureResultCode.UnsupportedConfiguration, observed);
            if (observed != request.ExpectedState) return new(CaptureResultCode.UnexpectedCurrentState, observed);
            if (request.Operation == CaptureOperation.ConfigureApplicationCrashDump) registry.Configure(request.TargetExecutable, request.DesiredState);
            else registry.Restore(request.TargetExecutable, request.DesiredState);
            var after = registry.ReadState(request.TargetExecutable);
            if (after != request.DesiredState) return RollbackAfterFailure(request, observed, request.DesiredState, CaptureResultCode.VerificationFailed);
            return new(CaptureResultCode.Success, after);
        }
        catch (UnauthorizedAccessException) { return RollbackAfterFailure(request, observed, request.DesiredState, CaptureResultCode.AccessDenied); }
        catch (PlatformNotSupportedException) { return new(CaptureResultCode.UnsupportedPlatform, observed); }
        catch (Exception) { return RollbackAfterFailure(request, observed, request.DesiredState, CaptureResultCode.ApplyFailed); }
    }
    private CaptureResult RollbackAfterFailure(CaptureRequest request, LocalDumpState? before, LocalDumpState desired, CaptureResultCode code)
    {
        if (before is null) return new(code);
        LocalDumpState? now = null;
        try
        {
            now = registry.ReadState(request.TargetExecutable);
            if (now.OtherValuesFingerprint != before.OtherValuesFingerprint || !IsTransitionState(now, before, desired)) return new(code, now, RollbackAttempted: false);
            registry.Restore(request.TargetExecutable, before);
            var restored = registry.ReadState(request.TargetExecutable);
            return restored == before ? new(code, now, RollbackAttempted: true, RollbackSucceeded: true) : new(CaptureResultCode.RollbackFailed, restored, RollbackAttempted: true);
        }
        catch { return new(CaptureResultCode.RollbackFailed, now, RollbackAttempted: now is not null); }
    }
    private static bool IsTransitionState(LocalDumpState now, LocalDumpState before, LocalDumpState desired) =>
        now.Supported && (now.KeyExists == before.KeyExists || now.KeyExists == desired.KeyExists) &&
        (now.DumpType == before.DumpType || now.DumpType == desired.DumpType) &&
        (now.DumpCount == before.DumpCount || now.DumpCount == desired.DumpCount) &&
        (now.DumpFolder == before.DumpFolder || now.DumpFolder == desired.DumpFolder);
    private static bool Validate(CaptureRequest? r)
    {
        if (r is null) return false;
        if (r.SchemaVersion != 1 || r.ActionId == Guid.Empty || !CrashCapturePolicy.IsValidExecutable(r.TargetExecutable) ||
            r.CreatedUtc < DateTimeOffset.UtcNow.AddMinutes(-5) || r.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            !CrashCapturePolicy.IsSupportedState(r.ExpectedState) || !CrashCapturePolicy.IsSupportedState(r.DesiredState)) return false;
        return r.Operation switch
        {
            CaptureOperation.ConfigureApplicationCrashDump => r.DesiredState == CrashCapturePolicy.Desired(r.ExpectedState),
            CaptureOperation.RestoreApplicationCrashDumpConfiguration => r.ExpectedState == CrashCapturePolicy.Desired(r.ExpectedState) && CrashCapturePolicy.IsSupportedState(r.DesiredState) && r.DesiredState.OtherValuesFingerprint == r.ExpectedState.OtherValuesFingerprint,
            _ => false
        };
    }
}
