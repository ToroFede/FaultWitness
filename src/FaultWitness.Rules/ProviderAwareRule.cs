using FaultWitness.Core;
using static FaultWitness.Rules.DiagnosticFacts;

namespace FaultWitness.Rules;

internal sealed class ProviderAwareRule(RuleDefinition definition) : IDiagnosticRule
{
    public string RuleId => definition.Id;
    public string Version => definition.Version;
    public IncidentCategory Category => definition.Category;
    public TimeSpan CorrelationWindow => TimeSpan.FromSeconds(definition.WindowSeconds);
    public string BuildSignature(NormalizedEvent diagnosticEvent) => Signature(diagnosticEvent);
    public bool SameOccurrence(NormalizedEvent left, NormalizedEvent right) => DiagnosticFacts.SameOccurrence(left, right);

    public bool AppliesTo(NormalizedEvent item) => RuleId switch
    {
        "power.planned_restart" or "power.update_restart" => IsPlanned(item),
        "power.unclean_shutdown" or "power.bugcheck_reboot" or "power.abrupt_restart_no_bugcheck" => IsReboot(item),
        "graphics.engine_timeout" => IsTdr(item) && LiveKernelCode(item) == "141",
        "graphics.adapter_timeout" => IsTdr(item) && LiveKernelCode(item) == "117",
        "graphics.tdr_with_driver_event" or "graphics.tdr_with_app_crash" or "graphics.repeated_timeout_pattern" => IsTdr(item),
        "hardware.whea.corrected" or "hardware.whea.recurrent_corrected" => IsCorrected(item),
        "hardware.whea.uncorrected" => IsUncorrected(item),
        "hardware.whea.processor_mce" => IsUncorrected(item) && IsProcessor(item),
        "hardware.whea.pcie" => IsPcie(item),
        "hardware.whea.memory" => IsUncorrected(item) && IsMemory(item),
        "hardware.whea_bugcheck_124" => IsBugcheck(item) && BugcheckCode(item) == 0x124,
        "application.crash" or "application.crash_with_wer" or "application.repeated_signature" => IsCrash(item) && !IsAudio(item),
        "application.third_party_faulting_module" => IsCrash(item) && !IsAudio(item) && IsThirdParty(item),
        "application.hang" => IsHang(item),
        "audio.audiodg_crash" => IsAudio(item),
        "audio.audiodg_third_party_module" or "audio.repeated_apo_failure" => IsApo(item),
        "storage.request_timeout" => IsStorage(item) && item.EventId == 129,
        "storage.io_retry" => IsStorage(item) && item.EventId == 153,
        "storage.io_warning" => IsStorage(item) && item.EventId == 51,
        "storage.repeated_timeout_pattern" => IsStorage(item) && item.EventId is 129 or 153,
        "resources.memory_exhaustion" or "resources.exhaustion_with_hang_or_crash" => IsExhaustion(item),
        "pnp.umdf_transient_load_warning" or "pnp.recurrent_device_failure" => IsPnp(item),
        "service.unexpected_termination" or "service.repeated_failure" => IsService(item),
        "system.bugcheck" or "system.bugcheck_with_dump" => IsBugcheck(item),
        _ => false
    };

    public bool RequirementsMet(NormalizedEvent anchor, RuleContext context)
    {
        // A sparse Reliability mirror must not become a second, differently classified anchor.
        if (anchor.SourceType == SourceType.Reliability && context.Nearby.Any(item =>
            item.SourceType != SourceType.Reliability && IsCrash(item) && SameOccurrence(anchor, item))) return false;
        var related = context.Nearby.Where(item => IsRelated(anchor, item)).ToArray();
        return RuleId switch
        {
            "power.bugcheck_reboot" => BugcheckCode(anchor) > 0 || related.Any(IsBugcheck),
            "power.abrupt_restart_no_bugcheck" => anchor.Field("BugcheckCode") == "0" && !context.Nearby.Any(IsBugcheck) &&
                CoveragePolicy.IsComplete(context.Coverage, SourceType.EventLog, "System", anchor.TimestampUtc - CorrelationWindow, anchor.TimestampUtc + CorrelationWindow) &&
                CoveragePolicy.IsComplete(context.Coverage, SourceType.Wer, null, anchor.TimestampUtc - CorrelationWindow, anchor.TimestampUtc + CorrelationWindow),
            "power.update_restart" => related.Any(item => IsUpdate(item) && item.TimestampUtc <= anchor.TimestampUtc),
            "graphics.tdr_with_driver_event" => related.Any(IsVendor),
            "graphics.tdr_with_app_crash" => related.Any(IsCrash),
            "application.crash_with_wer" => related.Any(IsWer) && related.Any(item => IsCrash(item) && !IsWer(item)),
            "application.repeated_signature" or "audio.repeated_apo_failure" =>
                Repeated(anchor, context.History),
            "graphics.repeated_timeout_pattern" => Vendor(anchor).Length > 0 && Repeated(anchor, context.History),
            "hardware.whea.recurrent_corrected" or "storage.repeated_timeout_pattern" => Repeated(anchor, related),
            "pnp.recurrent_device_failure" => Device(anchor).Length > 0 && Repeated(anchor, related) && !related.Any(IsDeviceStarted),
            "service.repeated_failure" => ServiceName(anchor).Length > 0 && Repeated(anchor, related) &&
                !related.Any(IsReboot) && related.Any(item => (IsCrash(item) || IsHang(item)) && item.TimestampUtc >= anchor.TimestampUtc),
            "resources.exhaustion_with_hang_or_crash" => related.Any(item => (IsCrash(item) || IsHang(item)) && item.TimestampUtc >= anchor.TimestampUtc),
            "system.bugcheck_with_dump" => related.Any(item => item.SourceType == SourceType.CrashArtifact),
            _ => true
        };
    }

    private bool Repeated(NormalizedEvent anchor, IEnumerable<NormalizedEvent> candidates) =>
        OccurrenceCount(candidates.Where(item => AppliesTo(item) && Signature(item) == Signature(anchor))) >= 2;

    public bool IsRelated(NormalizedEvent anchor, NormalizedEvent other)
    {
        if (anchor.Id == other.Id) return true;
        var seconds = (other.TimestampUtc - anchor.TimestampUtc).TotalSeconds;
        if (Math.Abs(seconds) > definition.WindowSeconds) return false;
        return Category switch
        {
            IncidentCategory.Power => IsPlanned(anchor)
                ? IsUpdate(other) && seconds <= 0
                : (IsBugcheck(other) && Math.Abs(seconds) <= 120) ||
                  ((IsWhea(other) || IsTdr(other) || IsStorage(other) || IsExhaustion(other) || IsPlanned(other)) && seconds <= 0) ||
                  (IsService(other) && seconds is >= 0 and <= 120),
            IncidentCategory.Graphics => SameOccurrence(anchor, other) ||
                (IsVendor(other) && VendorMatches(anchor, other)) || IsCrash(other),
            IncidentCategory.ApplicationCrash or IncidentCategory.ApplicationHang or IncidentCategory.Audio =>
                SameOccurrence(anchor, other) || (IsExhaustion(other) && seconds is >= -120 and <= 0),
            IncidentCategory.Hardware => IsWhea(other) && Signature(anchor) == Signature(other),
            IncidentCategory.Storage => IsStorage(other) && Device(anchor).Length > 0 && Equal(Device(anchor), Device(other)) &&
                Equal(anchor.Provider, other.Provider),
            IncidentCategory.Resources => (IsCrash(other) || IsHang(other)) && seconds >= 0,
            IncidentCategory.PlugAndPlay => Device(anchor).Length > 0 && Equal(Device(anchor), Device(other)) &&
                (IsPnp(other) || (IsDeviceStarted(other) && seconds >= 0)),
            IncidentCategory.Service => (IsService(other) && ServiceName(anchor).Length > 0 && Equal(ServiceName(anchor), ServiceName(other))) ||
                (IsReboot(other) && seconds <= 0) || ((IsCrash(other) || IsHang(other)) && seconds >= 0),
            IncidentCategory.BugCheck => SameOccurrence(anchor, other) || (IsWhea(other) && seconds <= 0) ||
                (other.SourceType == SourceType.CrashArtifact && Equal(other.Field("DumpKind"), "Kernel") &&
                 ((anchor.Field("DumpPath").Length > 0 && Equal(anchor.Field("DumpPath"), other.SourceReference)) ||
                  (ReportId(anchor).Length > 0 && Equal(ReportId(anchor), ReportId(other))))),
            _ => false
        };
    }

    private static bool VendorMatches(NormalizedEvent anchor, NormalizedEvent other) => Vendor(anchor).ToUpperInvariant() switch
    {
        "NVIDIA" => Equal(other.Provider, "nvlddmkm"),
        "AMD" => Equal(other.Provider, "amdkmdag") || Equal(other.Provider, "amdwddmg"),
        "INTEL" => Equal(other.Provider, "igfx"),
        _ => false
    };

    public Finding Evaluate(NormalizedEvent anchor, IReadOnlyList<NormalizedEvent> nearbyEvents)
    {
        var disposition = RuleId switch
        {
            "power.planned_restart" or "power.update_restart" => FindingDisposition.Expected,
            "pnp.umdf_transient_load_warning" => nearbyEvents.Any(IsDeviceStarted) ? FindingDisposition.Suppressed : FindingDisposition.Context,
            "storage.io_retry" or "storage.io_warning" => FindingDisposition.Context,
            "service.unexpected_termination" => nearbyEvents.Any(IsReboot) ? FindingDisposition.Context : FindingDisposition.Supporting,
            _ => FindingDisposition.Significant
        };
        return new Finding(RuleId, Version, disposition, definition.BaseStrength, definition.ObservedKey,
            definition.InterpretationKey, definition.NotEstablishedKey, [], [definition.RecommendedActionKey],
            definition.FalsePositiveContract) { Severity = definition.Severity };
    }

    public IEnumerable<Evidence> CoverageEvidence(NormalizedEvent anchor, RuleContext context)
    {
        var from = anchor.TimestampUtc - CorrelationWindow;
        var to = anchor.TimestampUtc + CorrelationWindow;
        if (IsReboot(anchor) && !context.Nearby.Any(item => IsWhea(item) && item.TimestampUtc <= anchor.TimestampUtc))
            yield return CoveragePolicy.Missing(context.Coverage, SourceType.EventLog, "System", from, anchor.TimestampUtc, "whea");
        if (IsReboot(anchor) || IsBugcheck(anchor))
            if (!context.Nearby.Any(item => IsRelated(anchor, item) && item.SourceType == SourceType.CrashArtifact))
                yield return CoveragePolicy.Missing(context.Coverage, SourceType.CrashArtifact, null, from, to, "dump");
        foreach (var coverage in context.Coverage.Where(static item => item.State != CoverageState.Complete))
            yield return new Evidence(EvidenceKind.Unknown, $"{coverage.SourceType}.{coverage.Channel}",
                "evidence.source.unknown", $"{coverage.SourceType}/{coverage.Channel}:{coverage.State}");
    }
}
