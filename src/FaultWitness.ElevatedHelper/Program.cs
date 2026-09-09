using System.Text.Json;
using System.Text.Json.Serialization;
using FaultWitness.Core;
using FaultWitness.Platform.Windows;

namespace FaultWitness.ElevatedHelper;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || args[0].Length is 0 or > CaptureRequestProtocol.MaxArgumentLength) return (int)CaptureResultCode.InvalidRequest;
            using var mutex = new Mutex(false, @"Global\FaultWitness.LocalDumps");
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(2))) return (int)CaptureResultCode.ApplyFailed; }
            catch (AbandonedMutexException) { }
            catch { return (int)CaptureResultCode.AccessDenied; }
            try { return CaptureExitProtocol.Encode(new LocalDumpCaptureEngine(new WindowsLocalDumpRegistry()).Execute(CaptureRequestProtocol.Parse(args[0]))); }
            finally { try { mutex.ReleaseMutex(); } catch { } }
        }
        catch (JsonException) { return (int)CaptureResultCode.InvalidRequest; }
        catch { return (int)CaptureResultCode.RegistryUnavailable; }
    }
}
