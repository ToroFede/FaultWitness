using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;

/// <summary>A one-shot UAC launch with a semantic request; never a persistent privileged command channel.</summary>
public sealed class ElevatedCaptureClient : ICrashCaptureService
{
    public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token)
        => Task.Run(() => new WindowsLocalDumpRegistry().Read(executable), token);

    public async Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new(CaptureResultCode.UnsupportedPlatform);
        if (!CrashCapturePolicy.IsValidExecutable(request.TargetExecutable) || !CrashCapturePolicy.IsSupportedState(request.ExpectedState) || !CrashCapturePolicy.IsSupportedState(request.DesiredState))
            return new(CaptureResultCode.InvalidRequest);
        var helperDirectory = Path.Combine(AppContext.BaseDirectory, "helper");
        var compatibility = CaptureHelperCompatibility.Validate(helperDirectory, CaptureHelperCompatibility.CurrentBuild, CaptureHelperCompatibility.CurrentRid);
        if (compatibility != CaptureResultCode.Success) return new(compatibility);
        var executable = Path.Combine(helperDirectory, "FaultWitness.ElevatedHelper.exe");
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request));
        if (encoded.Length > 12000) return new(CaptureResultCode.InvalidRequest);
        try
        {
            // This normal-user folder creation is not a capability of the elevated helper.
            if (request.Operation == CaptureOperation.ConfigureApplicationCrashDump)
                Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaultWitness", "Captures"));
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable, Arguments = encoded, UseShellExecute = true, Verb = "runas",
                WorkingDirectory = helperDirectory, WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return new(CaptureResultCode.ApplyFailed);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var response = CaptureExitProtocol.Decode(process.ExitCode);
            var code = response.Code;
            // Pending is never a completed helper result.
            if (code is CaptureResultCode.Pending or CaptureResultCode.AlreadyRestored) code = CaptureResultCode.ApplyFailed;
            var current = await ReadAsync(request.TargetExecutable, CancellationToken.None).ConfigureAwait(false);
            if (code == CaptureResultCode.Success && (current.Code != CaptureResultCode.Success || current.State != request.DesiredState))
                code = CaptureResultCode.VerificationFailed;
            return response with { Code = code, ObservedState = current.State, NativeError = response.NativeError ?? current.NativeError };
        }
        catch (Win32Exception exception) { return new(exception.NativeErrorCode == 1223 ? CaptureResultCode.CancelledByUser : exception.NativeErrorCode == 5 ? CaptureResultCode.AccessDenied : CaptureResultCode.ApplyFailed, NativeError: exception.NativeErrorCode); }
        catch (UnauthorizedAccessException exception) { return new(CaptureResultCode.AccessDenied, NativeError: exception.HResult); }
        catch (IOException exception) { return new(CaptureResultCode.ApplyFailed, NativeError: exception.HResult); }
    }
}
