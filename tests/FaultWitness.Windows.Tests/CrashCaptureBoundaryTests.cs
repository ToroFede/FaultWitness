using System.Text;
using System.Text.Json;
using FaultWitness.Core;
using FaultWitness.ElevatedHelper;
using FaultWitness.Platform.Windows;

namespace FaultWitness.Windows.Tests;

public sealed class CrashCaptureBoundaryTests
{
    private static LocalDumpState Empty => new(false);
    private static CaptureRequest Request(CaptureOperation op = CaptureOperation.ConfigureApplicationCrashDump, LocalDumpState? before = null, LocalDumpState? desired = null) {
        before ??= Empty; desired ??= op == CaptureOperation.ConfigureApplicationCrashDump ? CrashCapturePolicy.Desired(before) : before;
        return new(1, Guid.NewGuid(), op, "demo.exe", DateTimeOffset.UtcNow, before, desired);
    }
    [Theory]
    [InlineData("")]
    [InlineData("!")]
    [InlineData("YWJj")]
    public void ProtocolRejectsMalformed(string value) => Assert.False(CaptureRequestProtocol.TryParse(value, out _));
    [Fact] public void ProtocolRejectsDuplicateNestedField() {
        var json = "{\"SchemaVersion\":1,\"ActionId\":\""+Guid.NewGuid()+"\",\"Operation\":1,\"TargetExecutable\":\"demo.exe\",\"CreatedUtc\":\""+DateTimeOffset.UtcNow.ToString("O")+"\",\"ExpectedState\":{\"KeyExists\":false,\"KeyExists\":false,\"Supported\":true},\"DesiredState\":{\"KeyExists\":true}}";
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), out _));
    }
    [Theory]
    [InlineData(".exe", CaptureResultCode.InvalidRequest)]
    [InlineData("../x.exe", CaptureResultCode.InvalidRequest)]
    [InlineData(@"foo\bar.exe", CaptureResultCode.InvalidRequest)]
    [InlineData("*.exe", CaptureResultCode.InvalidRequest)]
    [InlineData("CON.exe", CaptureResultCode.InvalidRequest)]
    [InlineData("demo.exe", CaptureResultCode.UnexpectedCurrentState)]
    public void EngineEnforcesTargetAndCompare(string exe, CaptureResultCode expected) {
        var fake = new Fake(Empty); var r = Request(); r = r with { TargetExecutable = exe };
        if (exe == "demo.exe") r = r with { ExpectedState = CrashCapturePolicy.Desired(Empty), DesiredState = CrashCapturePolicy.Desired(CrashCapturePolicy.Desired(Empty)) };
        Assert.Equal(expected, new LocalDumpCaptureEngine(fake).Execute(r).Code);
    }
    [Fact] public void EngineRejectsStaleAndWrongDesired() {
        var r = Request() with { CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-6), DesiredState = new(true, 2, 3, CrashCapturePolicy.Folder) };
        Assert.Equal(CaptureResultCode.InvalidRequest, new LocalDumpCaptureEngine(new Fake(Empty)).Execute(r).Code);
    }
    [Fact] public void RestoreAbsentKeyIsAllowed() {
        var before = CrashCapturePolicy.Desired(Empty); var r = Request(CaptureOperation.RestoreApplicationCrashDumpConfiguration, before, Empty);
        var f = new Fake(before); Assert.Equal(CaptureResultCode.Success, new LocalDumpCaptureEngine(f).Execute(r).Code);
    }
    [Theory]
    [InlineData(0, 3)] [InlineData(1, 0)] [InlineData(1, 11)]
    public void EngineRejectsInvalidBoundedValues(int type, int count) {
        var bad = new LocalDumpState(true, type, count, CrashCapturePolicy.Folder);
        Assert.Equal(CaptureResultCode.InvalidRequest, new LocalDumpCaptureEngine(new Fake(Empty)).Execute(Request() with { DesiredState = bad }).Code);
    }
    [Fact] public void EngineRejectsArbitraryFolder() {
        var bad = new LocalDumpState(true, 1, 3, @"C:\private\dumps");
        Assert.Equal(CaptureResultCode.InvalidRequest, new LocalDumpCaptureEngine(new Fake(Empty)).Execute(Request() with { DesiredState = bad }).Code);
    }
    [Fact] public void EngineRejectsFutureAndEmptyAction() {
        var e = new LocalDumpCaptureEngine(new Fake(Empty));
        Assert.Equal(CaptureResultCode.InvalidRequest, e.Execute(Request() with { CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(2) }).Code);
        Assert.Equal(CaptureResultCode.InvalidRequest, e.Execute(Request() with { ActionId = Guid.Empty }).Code);
    }
    [Fact] public void ProtocolRejectsUnknownAndOversize() {
        Assert.False(CaptureRequestProtocol.TryParse(new string('A', 12001), out _));
        var bytes = Encoding.UTF8.GetBytes("{\"Nope\":1}");
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(bytes), out _));
    }
    [Fact] public void ProtocolRejectsDuplicateTopLevelUnknownNestedMissingAndWhitespace() {
        var valid = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(Request()));
        var duplicate = valid.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":1,\"SchemaVersion\":1", StringComparison.Ordinal);
        var marker = "\"Supported\":true";
        var index = valid.IndexOf(marker, StringComparison.Ordinal);
        var unknownNested = valid.Insert(index + marker.Length, ",\"Unexpected\":false");
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(Encoding.UTF8.GetBytes(duplicate)), out _));
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(Encoding.UTF8.GetBytes(unknownNested)), out _));
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"SchemaVersion\":1}")), out _));
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Request())) + " ", out _));
    }
    [Fact] public void ProtocolRejectsInvalidSemanticStateAndOperation() {
        var badFingerprint = new LocalDumpState(true, 1, 3, CrashCapturePolicy.Folder, "not-a-fingerprint");
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Request() with { DesiredState = badFingerprint })), out _));
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Request() with { Operation = (CaptureOperation)99 })), out _));
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Request() with { SchemaVersion = 2 })), out _));
        Assert.False(CaptureRequestProtocol.TryParse(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Request() with { TargetExecutable = "..\\demo.exe" })), out _));
    }
    [Fact] public void PartialWriteRollsBackWhenStateRemainsBounded() {
        var f = new Fake(Empty) { ThrowAfterConfigure = true };
        var result = new LocalDumpCaptureEngine(f).Execute(Request());
        Assert.True(result.RollbackAttempted); Assert.True(result.RollbackSucceeded); Assert.Equal(Empty, f.State);
    }
    [Fact] public void ExternalFingerprintDriftBlocksRollback() {
        var f = new Fake(Empty) { ThrowAfterConfigure = true, DriftOnRead = true };
        var result = new LocalDumpCaptureEngine(f).Execute(Request());
        Assert.False(result.RollbackSucceeded);
    }
    [Fact] public void ExitProtocolRoundTripsFlagsAndNativeError() {
        var encoded = CaptureExitProtocol.Encode(new CaptureResult(CaptureResultCode.AccessDenied, NativeError: 5, RollbackAttempted: true, RollbackSucceeded: true));
        var decoded = CaptureExitProtocol.Decode(encoded);
        Assert.Equal(CaptureResultCode.AccessDenied, decoded.Code); Assert.Equal(5, decoded.NativeError);
        Assert.True(decoded.RollbackAttempted); Assert.True(decoded.RollbackSucceeded);
    }
    [Fact] public void ReadMapsAccessDeniedAndUnavailable() {
        var denied = new Fake(Empty) { ReadFailure = new UnauthorizedAccessException() };
        Assert.Equal(CaptureResultCode.AccessDenied, new LocalDumpCaptureEngine(denied).Read("demo.exe").Code);
        var unavailable = new Fake(Empty) { ReadFailure = new InvalidOperationException() };
        Assert.Equal(CaptureResultCode.RegistryUnavailable, new LocalDumpCaptureEngine(unavailable).Read("demo.exe").Code);
        var unsupported = new Fake(Empty) { ReadFailure = new PlatformNotSupportedException() };
        Assert.Equal(CaptureResultCode.UnsupportedPlatform, new LocalDumpCaptureEngine(unsupported).Read("demo.exe").Code);
    }
    [Fact] public void ReadMatrixPreservesSafeStateClassification() {
        var states = new[] {
            new LocalDumpState(false),
            new LocalDumpState(true),
            new LocalDumpState(true, 2, 5, @"%LOCALAPPDATA%\CrashDumps"),
            new LocalDumpState(true, 1),
            new LocalDumpState(true, null, null, null, "", false)
        };
        foreach (var state in states) Assert.Equal(state, Assert.IsType<LocalDumpState>(new LocalDumpCaptureEngine(new Fake(state)).Read("demo.exe").State));
    }
    [Fact] public void ConfigurePreservesPreviouslySupportedUnrelatedState() {
        var before = new LocalDumpState(true, 2, 5, @"%LOCALAPPDATA%\CrashDumps", new string('A', 64));
        var fake = new Fake(before); var result = new LocalDumpCaptureEngine(fake).Execute(Request(before: before));
        Assert.Equal(CaptureResultCode.Success, result.Code); Assert.Equal(new LocalDumpState(true, 1, 3, CrashCapturePolicy.Folder, before.OtherValuesFingerprint), fake.State);
    }
    [Fact] public void RestorePreservesOriginallyEmptyExistingKey() {
        var prior = new LocalDumpState(true); var configured = CrashCapturePolicy.Desired(prior); var fake = new Fake(configured);
        var result = new LocalDumpCaptureEngine(fake).Execute(Request(CaptureOperation.RestoreApplicationCrashDumpConfiguration, configured, prior));
        Assert.Equal(CaptureResultCode.Success, result.Code); Assert.Equal(prior, fake.State);
    }
    [Fact] public void NullRequestIsInvalid() => Assert.Equal(CaptureResultCode.InvalidRequest, new LocalDumpCaptureEngine(new Fake(Empty)).Execute(null!).Code);
    [Fact] public void VerificationFailureIsReported() {
        var f = new Fake(Empty) { VerificationFailure = true };
        Assert.Equal(CaptureResultCode.VerificationFailed, new LocalDumpCaptureEngine(f).Execute(Request()).Code);
    }
    [Fact] public void UnsupportedReadStateIsNotShownAsActive() {
        var read = new CaptureReadResult(CaptureResultCode.Success, new LocalDumpState(true, null, null, null, "", false));
        Assert.Equal(CaptureActiveState.UnsupportedConfiguration, CrashCapturePolicy.Active(read));
    }
    [Fact] public void RollbackFailureIsReported() {
        var f = new Fake(Empty) { ThrowAfterConfigure = true, RollbackFailure = true };
        var result = new LocalDumpCaptureEngine(f).Execute(Request());
        Assert.Equal(CaptureResultCode.RollbackFailed, result.Code); Assert.Equal(CrashCapturePolicy.Desired(Empty), result.ObservedState);
    }
    private sealed class Fake(LocalDumpState state) : ILocalDumpRegistry {
        public LocalDumpState State { get; private set; } = state;
        public bool ThrowAfterConfigure; public bool DriftOnRead; public bool VerificationFailure; public bool RollbackFailure; public Exception? ReadFailure;
        public LocalDumpState ReadState(string executable) { if (ReadFailure is not null) throw ReadFailure; return VerificationFailure && State.KeyExists ? State with { DumpType = 2 } : DriftOnRead && State.KeyExists ? State with { OtherValuesFingerprint = "F" } : State; }
        public void Configure(string executable, LocalDumpState desired) { State = desired; if (ThrowAfterConfigure) throw new InvalidOperationException(); }
        public void Restore(string executable, LocalDumpState prior) { if (RollbackFailure) throw new InvalidOperationException(); State = prior; }
    }
}
