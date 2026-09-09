using FaultWitness.Core;

namespace FaultWitness.Core.Tests;

public sealed class ChangeCorrelatorTests
{
    private static readonly DateTimeOffset Onset = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ChangeCategory.DriverInstalled, ChangeSubsystem.Display)]
    [InlineData(ChangeCategory.WindowsUpdate, ChangeSubsystem.System)]
    [InlineData(ChangeCategory.DriverInstalled, ChangeSubsystem.Storage)]
    public void NearbyDriverUpdateWindowsUpdateOrDeviceInstallationNeverChangesDiagnosticClaims(ChangeCategory category, ChangeSubsystem subsystem)
    {
        var incident = Incident(IncidentCategory.Graphics, Onset, "graphics.signature");
        incident = incident with { Findings = incident.Findings.Select(item => item with { Strength = EvidenceStrength.Limited }).ToArray() };
        var result = Attach(incident, Change("near", Onset.AddHours(-1), category, subsystem));
        var actual = Assert.Single(result.Incidents);
        Assert.Single(actual.RelatedChanges);
        Assert.Equal(EvidenceStrength.Limited, actual.EvidenceStrength);
        Assert.Same(incident.Findings, actual.Findings);
        Assert.Same(incident.Evidence, actual.Evidence);
        Assert.Same(incident.Relations, actual.Relations);
        Assert.Same(incident.SourceEvents, actual.SourceEvents);
    }

    [Fact]
    public void GraphicsFirstOccurrenceNoChanges()
    {
        var scan = Scan(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"));
        var result = ChangeCorrelator.Attach(scan, [], new ChangeHistoryBatch([], []));

        var incident = Assert.Single(result.Incidents);
        Assert.Empty(incident.RelatedChanges);
        Assert.Equal(Onset, incident.ChangeContext!.FirstObservedUtc);
        Assert.Equal(FirstObservationBasis.CurrentScan, incident.ChangeContext.Basis);
        Assert.False(incident.ChangeContext.IsRecurring);
    }

    [Fact]
    public void GraphicsDriverUpdatePreviousDay_RecordsObservedInstallVersionWithoutInventingPrevious()
    {
        var change = Change("display-driver", Onset.AddDays(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display,
            previous: null, next: "31.0.15.5212", identity: "PCI\\VEN_10DE&DEV_2684");
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "PCI\\VEN_10DE&DEV_2684"), change);

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ContextualRelevance.High, related.Relevance);
        Assert.Equal(ChangeTiming.Before, related.Timing);
        Assert.Null(related.Change.PreviousValue);
        Assert.Equal("31.0.15.5212", related.Change.NewValue);
    }

    [Fact]
    public void GraphicsUnrelatedPrinterDriverChange_IsLowRelevance()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"),
            Change("printer-driver", Onset.AddHours(-2), ChangeCategory.DriverInstalled, ChangeSubsystem.Printer, next: "4.2"));

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ContextualRelevance.Low, related.Relevance);
        Assert.Equal("ChangeReasonOtherSubsystem", related.ReasonKey);
    }

    [Fact]
    public void GraphicsWindowsUpdateSameDay_IsSystemContext()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"),
            Change("kb", Onset.AddHours(-4), ChangeCategory.WindowsUpdate, ChangeSubsystem.System, next: "KB6000001"));

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ContextualRelevance.Moderate, related.Relevance);
        Assert.Equal("ChangeReasonSystemUpdate", related.ReasonKey);
        Assert.Equal(ChangeTiming.Before, related.Timing);
    }

    [Fact]
    public void AudioDriverChangeBeforeAudioPattern_IsRelevant()
    {
        var incident = Incident(IncidentCategory.Audio, Onset, "audio.signature");
        var scan = Scan(incident) with { Patterns = [new RecurringPattern("audio.signature", IncidentCategory.Audio, [incident.Id])] };
        var result = ChangeCorrelator.Attach(scan, [], new ChangeHistoryBatch([
            Change("audio-driver", Onset.AddHours(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Audio, next: "10.0")
        ], []));

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ContextualRelevance.High, related.Relevance);
        Assert.Equal(ChangeTiming.Before, related.Timing);
    }

    [Fact]
    public void StorageControllerChangeBeforeStoragePattern_IsRelevant()
    {
        var incident = Incident(IncidentCategory.Storage, Onset, "storage.signature");
        var scan = Scan(incident) with { Patterns = [new RecurringPattern("storage.signature", IncidentCategory.Storage, [incident.Id])] };
        var result = ChangeCorrelator.Attach(scan, [], new ChangeHistoryBatch([
            Change("storage-controller", Onset.AddHours(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Storage, next: "enabled")
        ], []));

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ContextualRelevance.High, related.Relevance);
        Assert.Equal(ChangeTiming.Before, related.Timing);
    }

    [Fact]
    public void ChangeAfterIncident_IsAfterAndLowAndCannotBePrecursor()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"),
            Change("later-driver", Onset.AddHours(2), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "2"));

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ChangeTiming.After, related.Timing);
        Assert.Equal(ContextualRelevance.Low, related.Relevance);
        Assert.Equal("ChangeReasonAfter", related.ReasonKey);
    }

    [Fact]
    public void MultipleChanges_AreRankedByContextualRelevanceThenDistance()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "GPU-1"),
            Change("other", Onset.AddMinutes(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Printer, next: "p"),
            Change("system", Onset.AddMinutes(-2), ChangeCategory.WindowsUpdate, ChangeSubsystem.System, next: "KB"),
            Change("display-far", Onset.AddDays(-3), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "old"),
            Change("display-near", Onset.AddHours(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "new", identity: "GPU-1"));

        Assert.Equal(["display-near", "system", "display-far", "other"], result.Incidents[0].RelatedChanges.Select(x => x.Change.Id));
        Assert.Equal([ContextualRelevance.High, ContextualRelevance.Moderate, ContextualRelevance.Moderate, ContextualRelevance.Low],
            result.Incidents[0].RelatedChanges.Select(x => x.Relevance));
    }

    [Fact]
    public void FirstOccurrenceUsesRetainedHistory()
    {
        var retained = new RetainedOccurrence("graphics.signature", IncidentCategory.Graphics, Onset.AddDays(-2));
        var result = ChangeCorrelator.Attach(Scan(Incident(IncidentCategory.Graphics, Onset, "graphics.signature")), [retained], new ChangeHistoryBatch([], []));

        var context = result.Incidents[0].ChangeContext!;
        Assert.Equal(retained.TimestampUtc, context.FirstObservedUtc);
        Assert.Equal(FirstObservationBasis.RetainedHistory, context.Basis);
        Assert.True(context.IsRecurring);
    }

    [Fact]
    public void FirstOccurrenceOnlyKnownInCurrentScan()
    {
        var result = ChangeCorrelator.Attach(Scan(Incident(IncidentCategory.Graphics, Onset, "graphics.signature")), [], new ChangeHistoryBatch([], []));

        var context = result.Incidents[0].ChangeContext!;
        Assert.Equal(Onset, context.FirstObservedUtc);
        Assert.Equal(FirstObservationBasis.CurrentScan, context.Basis);
        Assert.False(context.IsRecurring);
    }

    [Fact]
    public void DuplicateRetainedOccurrencesAtSameTimestampDoNotFakeRecurrence()
    {
        var retained = new[] {
            new RetainedOccurrence("graphics.signature", IncidentCategory.Graphics, Onset),
            new RetainedOccurrence("graphics.signature", IncidentCategory.Graphics, Onset)
        };
        var result = ChangeCorrelator.Attach(Scan(Incident(IncidentCategory.Graphics, Onset, "graphics.signature")), retained, new ChangeHistoryBatch([], []));

        Assert.False(result.Incidents[0].ChangeContext!.IsRecurring);
    }

    [Fact]
    public void PartialChangeSourceCoverage_IsPreservedForRelevantWindow()
    {
        var coverage = new SourceCoverage(SourceType.ChangeHistory, CoverageState.Partial, Onset.AddDays(-1), Onset.AddHours(1), "changes.partial");
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"), [], coverage);

        var actual = Assert.Single(result.Incidents[0].ChangeContext!.Coverage);
        Assert.Equal(CoverageState.Partial, actual.State);
        Assert.Equal("changes.partial", actual.DetailKey);
    }

    [Fact]
    public void AllChangeSourcesUnavailable_IsPreserved()
    {
        var coverage = new SourceCoverage(SourceType.ChangeHistory, CoverageState.Unavailable, null, null, "changes.unavailable");
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"), [], coverage);

        Assert.Equal(CoverageState.Unavailable, Assert.Single(result.Incidents[0].ChangeContext!.Coverage).State);
    }

    [Fact]
    public void DuplicateChangeRecords_DeduplicateByIdAndOperation()
    {
        var first = Change("source-a", Onset.AddMinutes(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "2", identity: "GPU-1", source: "eventlog", sourceReference: "r1", classId: "display");
        var sameId = first with { Source = "inventory", SourceReference = "r2" };
        var sameOperation = first with { Id = "source-b", Source = "inventory", SourceReference = "r3" };
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"), first, sameId, sameOperation);

        Assert.Single(result.Incidents[0].RelatedChanges);
    }

    [Fact]
    public void SameDriverEventRepresentedByMultipleSources_DeduplicatesOnlyWhenIdentityAndVersionMatch()
    {
        var eventLog = Change("event", Onset.AddMinutes(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "2", identity: "GPU-1", source: "eventlog", sourceReference: "r1", classId: "display");
        var inventory = Change("inventory", Onset.AddMinutes(-1).AddSeconds(30), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "2", identity: "GPU-1", source: "inventory", sourceReference: "r2", classId: "display");
        var differentVersion = inventory with { Id = "inventory-new", NewValue = "3" };
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"), eventLog, inventory, differentVersion);

        Assert.Equal(["inventory-new", "event"], result.Incidents[0].RelatedChanges.Select(x => x.Change.Id));
    }

    [Fact]
    public void ExactWindowBoundariesAreInclusiveAndCategorySpecific()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"),
            Change("driver-start", Onset.AddDays(-7), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "1"),
            Change("driver-end", Onset.AddDays(1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "2"),
            Change("update-out", Onset.AddDays(-3).AddSeconds(-1), ChangeCategory.WindowsUpdate, ChangeSubsystem.System, next: "KB"),
            Change("device-end", Onset.AddDays(1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "x"),
            Change("device-out", Onset.AddDays(1).AddSeconds(1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "y"));

        Assert.Equal(["driver-start", "device-end", "driver-end"], result.Incidents[0].RelatedChanges.Select(x => x.Change.Id));
    }

    [Fact]
    public void CancellationIsObservedBeforeAttachingEachIncident()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var scan = Scan(Incident(IncidentCategory.Graphics, Onset, "graphics.signature"));

        Assert.Throws<OperationCanceledException>(() => ChangeCorrelator.Attach(scan, [], new ChangeHistoryBatch([], []), cancellation.Token));
    }

    [Fact]
    public void AttachPreservesFindingsEvidenceRelationsAndPatterns()
    {
        var incident = Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "GPU-1") with
        {
            Relations = [new EventRelation(Guid.NewGuid(), Guid.NewGuid(), RelationKind.SameDevice)]
        };
        var scan = Scan(incident) with { Patterns = [new RecurringPattern("graphics.signature", IncidentCategory.Graphics, [incident.Id])] };
        var result = ChangeCorrelator.Attach(scan, [], new ChangeHistoryBatch([], []));
        var actual = result.Incidents[0];

        Assert.Equal(incident.Findings, actual.Findings);
        Assert.Equal(incident.Evidence, actual.Evidence);
        Assert.Equal(incident.Relations, actual.Relations);
        Assert.Equal(scan.Patterns, result.Patterns);
    }

    [Fact]
    public void DeviceIdentityMatchIsExplicitAndMismatchDoesNotBecomeRelevant()
    {
        var match = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "GPU-1"),
            Change("match", Onset.AddHours(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Unknown, next: "x", identity: "GPU-1"));
        var mismatch = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "GPU-1"),
            Change("mismatch", Onset.AddHours(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Display, next: "x", identity: "GPU-2"));

        Assert.Equal(ContextualRelevance.High, Assert.Single(match.Incidents[0].RelatedChanges).Relevance);
        Assert.Equal(ContextualRelevance.Low, Assert.Single(mismatch.Incidents[0].RelatedChanges).Relevance);
    }

    [Fact]
    public void ExplicitMismatchAlsoOverridesBroadUpdateRelevance()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "GPU-1"),
            Change("update", Onset.AddHours(-1), ChangeCategory.WindowsUpdate, ChangeSubsystem.System, identity: "GPU-2"));
        Assert.Equal(ContextualRelevance.Low, Assert.Single(result.Incidents[0].RelatedChanges).Relevance);
    }

    [Fact]
    public void VendorSubjectAndProseDoNotCreateFuzzyOrImportedIdentityMatches()
    {
        var result = Attach(Incident(IncidentCategory.Graphics, Onset, "graphics.signature", "PCI\\GPU-1"),
            Change("prose", Onset.AddHours(-1), ChangeCategory.DriverInstalled, ChangeSubsystem.Unknown, next: "2", identity: "GPU-1", vendor: "PCI\\GPU-1 Display Vendor", source: "imported"));

        var related = Assert.Single(result.Incidents[0].RelatedChanges);
        Assert.Equal(ContextualRelevance.Low, related.Relevance);
        Assert.Equal("ChangeReasonOtherSubsystem", related.ReasonKey);
    }

    private static ScanResult Attach(Incident incident, params SystemChange[] changes) =>
        ChangeCorrelator.Attach(Scan(incident), [], new ChangeHistoryBatch(changes, []) );

    private static ScanResult Attach(Incident incident, SystemChange[] changes, params SourceCoverage[] coverage) =>
        ChangeCorrelator.Attach(Scan(incident), [], new ChangeHistoryBatch(changes, coverage));

    private static ScanResult Scan(params Incident[] incidents) =>
        new(incidents, [], Onset, Onset.AddMinutes(1));

    private static Incident Incident(IncidentCategory category, DateTimeOffset at, string signature, string? deviceId = null)
    {
        var anchor = new NormalizedEvent(Guid.NewGuid(), SourceType.EventLog, "Windows", at, "System", "FaultWitness.Tests", 1, null,
            IncidentSeverity.High, null, null, null, null,
            deviceId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["DeviceInstanceId"] = deviceId }, "anchor");
        return new(Guid.NewGuid(), at, at, category, IncidentSeverity.High, anchor,
            [new Evidence(EvidenceKind.Positive, "test", "evidence.test", "fixture", anchor)],
            [new Finding("test.rule", "1", FindingDisposition.Significant, EvidenceStrength.Strong, "observed", "interpreted", "unknown", ["hypothesis"], ["action"], ["contract"])],
            [anchor], [], signature);
    }

    private static SystemChange Change(string id, DateTimeOffset at, ChangeCategory category, ChangeSubsystem subsystem,
        string? previous = "old", string? next = "new", string? identity = null, string source = "history",
        string? sourceReference = null, string? classId = null, string? vendor = null) =>
        new SystemChange(id, "Windows", at, category, source, "fixture subject", subsystem, previous, next, vendor,
            sourceReference ?? id) with { ComponentIdentity = identity, ClassId = classId };
}
