using FaultWitness.Core;

namespace FaultWitness.Rules;

/// <summary>Equivalent metadata for all rules; complex predicates remain readable code.</summary>
public sealed record RuleDefinition(string Id, IncidentCategory Category, string Anchor,
    string RequiredEvidence, string OptionalSupportingEvidence, int WindowSeconds,
    EvidenceStrength BaseStrength, IncidentSeverity Severity, string[] FalsePositiveContract)
{
    public string Version { get; init; } = "0.9.1";
    public string Platform { get; init; } = "Windows";
    public string ObservedKey => $"rule.{Id}.observed";
    public string InterpretationKey => $"rule.{Id}.interpretation";
    public string NotEstablishedKey => $"rule.{Id}.not_established";
    public string RecommendedActionKey => $"action.{Category}.investigate";
    public string SuppressionConditions => Id switch
    {
        "power.planned_restart" or "power.update_restart" => "Expected restart context; never a headline.",
        "pnp.umdf_transient_load_warning" => "Suppressed after same-device Configuration 410 success within 300 s; otherwise context.",
        "pnp.recurrent_device_failure" => "Unknown device, missing repetition or observed same-device success prevents matching.",
        "storage.io_retry" or "storage.io_warning" => "Context only; single record is not a headline.",
        "service.unexpected_termination" => "Context if reboot precedes by at most 300 s; otherwise supporting, not headline.",
        "service.repeated_failure" => "Preceding reboot, unknown service or missing repetition/following application failure prevents matching.",
        _ => "Unmet required evidence prevents matching. Sparse Reliability mirrors use the original crash anchor."
    };
}

public static class RuleCatalog
{
    public const string DatabaseVersion = "0.9.1";
    public static IReadOnlyList<IDiagnosticRule> CreateDefault() => Definitions.Select(static definition => (IDiagnosticRule)new ProviderAwareRule(definition)).ToArray();
    public static IReadOnlyList<RuleDefinition> Definitions { get; } =
    [
        new("power.unclean_shutdown", IncidentCategory.Power, "Kernel-Power 41 in System",
            "none", "WHEA, bugcheck, storage or graphics context; pre 300 s / post 120 s", 300, EvidenceStrength.Limited, IncidentSeverity.Medium, ["psu_failure", "wall_power_loss", "hardware_defect"]),
        new("power.bugcheck_reboot", IncidentCategory.Power, "Kernel-Power 41 in System",
            "nonzero structured BugcheckCode OR matching System bugcheck ±120 s", "WHEA before reboot", 300, EvidenceStrength.Moderate, IncidentSeverity.High, ["bugcheck_cause"]),
        new("power.planned_restart", IncidentCategory.Power, "USER32 1074 in System",
            "none", "restart reason fields", 300, EvidenceStrength.Strong, IncidentSeverity.Informational, ["unexpected_shutdown"]),
        new("power.update_restart", IncidentCategory.Power, "USER32 1074 in System",
            "WindowsUpdateClient 19/21 in preceding 300 s", "restart reason", 300, EvidenceStrength.Moderate, IncidentSeverity.Low, ["cause_of_other_incident"]),
        new("power.abrupt_restart_no_bugcheck", IncidentCategory.Power, "Kernel-Power 41 in System",
            "BugcheckCode=0, no matching bugcheck; complete System+WER coverage over ±300 s", "unknown dump availability retained", 300, EvidenceStrength.Limited, IncidentSeverity.Medium, ["power_supply_failure"]),
        new("graphics.engine_timeout", IncidentCategory.Graphics, "WER Application 1001 or structured WER file",
            "EventType=LiveKernelEvent and code 141", "vendor event, application crash", 120, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["physical_gpu_failure"]),
        new("graphics.adapter_timeout", IncidentCategory.Graphics, "WER Application 1001 or structured WER file",
            "EventType=LiveKernelEvent and code 117", "vendor event, application crash", 120, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["physical_gpu_failure"]),
        new("graphics.tdr_with_driver_event", IncidentCategory.Graphics, "Structured LiveKernelEvent 141/117",
            "known vendor and matching display-provider event within ±120 s", "application crash context", 120, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["universal_vendor_event_meaning"]),
        new("graphics.tdr_with_app_crash", IncidentCategory.Graphics, "Structured LiveKernelEvent 141/117",
            "application crash within ±120 s", "process/module identity", 120, EvidenceStrength.Limited, IncidentSeverity.Medium, ["physical_gpu_failure"]),
        new("graphics.repeated_timeout_pattern", IncidentCategory.Graphics, "Structured LiveKernelEvent 141/117",
            "two distinct exact signatures in scan history; known vendor", "nearby driver/app records", 120, EvidenceStrength.Moderate, IncidentSeverity.High, ["physical_gpu_failure"]),
        new("hardware.whea.corrected", IncidentCategory.Hardware, "WHEA System record",
            "structured Corrected severity OR recognized corrected event 17/19/47", "error source/component fields", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["hardware_replacement"]),
        new("hardware.whea.recurrent_corrected", IncidentCategory.Hardware, "Corrected WHEA record",
            "two distinct exact signatures within ±300 s", "structured stable component identity", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["hardware_replacement"]),
        new("hardware.whea.uncorrected", IncidentCategory.Hardware, "WHEA System record",
            "Fatal/Recoverable severity OR recognized uncorrected event 18/20/46", "error record fields", 300, EvidenceStrength.Strong, IncidentSeverity.High, ["defective_cpu"]),
        new("hardware.whea.processor_mce", IncidentCategory.Hardware, "WHEA System record",
            "Uncorrected record with MachineCheck source OR MCA bank/status fields", "APIC, bank, status", 300, EvidenceStrength.Moderate, IncidentSeverity.High, ["defective_cpu"]),
        new("hardware.whea.pcie", IncidentCategory.Hardware, "WHEA System record",
            "PCIe component OR PortType+Bus fields", "segment/device/function", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["defective_device"]),
        new("hardware.whea.memory", IncidentCategory.Hardware, "WHEA System record",
            "Uncorrected record with Memory component OR physical address+mask", "error source", 300, EvidenceStrength.Moderate, IncidentSeverity.High, ["defective_memory"]),
        new("hardware.whea_bugcheck_124", IncidentCategory.BugCheck, "System WER-SystemErrorReporting/BugCheck 1001",
            "structured bugcheck code 0x124 (292 decimal)", "preceding WHEA within 300 s", 300, EvidenceStrength.Strong, IncidentSeverity.High, ["specific_component_failure"]),
        new("application.crash", IncidentCategory.ApplicationCrash, "Application Error 1000 / typed WER crash / structured Reliability mirror",
            "crash type, not hang/TDR; excludes audiodg handled by audio", "same-report representations", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["faulting_module_root_cause"]),
        new("application.crash_with_wer", IncidentCategory.ApplicationCrash, "Application crash",
            "same-occurrence WER and non-WER representations", "Reliability mirror", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["duplicate_confidence"]),
        new("application.third_party_faulting_module", IncidentCategory.ApplicationCrash, "Application crash",
            "ModuleTrust=VerifiedThirdParty; module present", "module metadata", 300, EvidenceStrength.Limited, IncidentSeverity.Medium, ["faulting_module_root_cause"]),
        new("application.repeated_signature", IncidentCategory.ApplicationCrash, "Application crash",
            "two distinct exact signatures in scan history", "version/module/exception/bucket", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["cause_certainty"]),
        new("application.hang", IncidentCategory.ApplicationHang, "Application Hang 1002 / typed WER hang",
            "hang type distinct from crash", "preceding exhaustion", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["application_crash"]),
        new("audio.audiodg_crash", IncidentCategory.Audio, "Application crash in audiodg.exe",
            "process identity", "module fields", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["audio_driver_cause"]),
        new("audio.audiodg_third_party_module", IncidentCategory.Audio, "Audio host crash",
            "verified third-party module and explicit APO role", "module version", 300, EvidenceStrength.Limited, IncidentSeverity.Medium, ["module_caused_crash"]),
        new("audio.repeated_apo_failure", IncidentCategory.Audio, "Audio host crash with verified APO",
            "two distinct exact signatures in scan history", "exception/version", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["module_caused_crash"]),
        new("storage.request_timeout", IncidentCategory.Storage, "System storahci/stornvme/iaStorA/iaStorAC/StorPort 129",
            "provider+channel identity", "same-device retries", 300, EvidenceStrength.Limited, IncidentSeverity.Medium, ["drive_failure"]),
        new("storage.io_retry", IncidentCategory.Storage, "System Disk 153",
            "provider+channel identity", "same-device retries", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["drive_failure"]),
        new("storage.io_warning", IncidentCategory.Storage, "System Disk 51",
            "provider+channel identity", "same-device context", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["drive_failure"]),
        new("storage.repeated_timeout_pattern", IncidentCategory.Storage, "Recognized System storage 129/153",
            "two exact signatures with known device within ±300 s", "same-device storage context", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["drive_failure"]),
        new("resources.memory_exhaustion", IncidentCategory.Resources, "System Resource-Exhaustion-Detector 2004",
            "provider+channel identity", "following application failure", 120, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["hardware_defect"]),
        new("resources.exhaustion_with_hang_or_crash", IncidentCategory.Resources, "Resource exhaustion",
            "application hang/crash follows within 120 s", "process context", 120, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["cause_certainty"]),
        new("pnp.umdf_transient_load_warning", IncidentCategory.PlugAndPlay, "System Kernel-PnP 219",
            "provider+channel identity", "same-device Configuration 410 success within 300 s", 300, EvidenceStrength.Insufficient, IncidentSeverity.Informational, ["broken_usb_device"]),
        new("pnp.recurrent_device_failure", IncidentCategory.PlugAndPlay, "System Kernel-PnP 219",
            "known device, two warnings ±300 s, no observed same-device success", "PnP context", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["broken_usb_device"]),
        new("service.unexpected_termination", IncidentCategory.Service, "System Service Control Manager 7031/7034",
            "provider+channel identity", "reboot within preceding 300 s", 300, EvidenceStrength.Limited, IncidentSeverity.Low, ["root_cause"]),
        new("service.repeated_failure", IncidentCategory.Service, "Service termination",
            "same service twice ±300 s, future crash/hang within 300 s; no preceding reboot", "service name", 300, EvidenceStrength.Moderate, IncidentSeverity.Medium, ["root_cause"]),
        new("system.bugcheck", IncidentCategory.BugCheck, "System WER-SystemErrorReporting/BugCheck 1001",
            "nonzero structured bugcheck code", "WHEA, artifact", 300, EvidenceStrength.Strong, IncidentSeverity.High, ["ntoskrnl_root_cause"]),
        new("system.bugcheck_with_dump", IncidentCategory.BugCheck, "System bugcheck",
            "matching discovered kernel dump path or report ID within ±300 s", "bugcheck parameters", 300, EvidenceStrength.Strong, IncidentSeverity.High, ["dump_analysis_complete"]),
    ];
}
