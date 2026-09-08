using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.Rules.Tests;

public sealed class DiagnosticRegressionTests
{
    [Theory]
    [InlineData("nvidia_tdr_141")]
    [InlineData("amd_tdr_141")]
    [InlineData("intel_tdr_141")]
    public void VendorTdr_RequiresTypedReportAndMatchingDriver(string fixture)
    {
        RuleAssertions.Present(fixture, "graphics.engine_timeout");
        RuleAssertions.Present(fixture, "graphics.tdr_with_driver_event");
    }

    [Fact]
    public void WheaBusInterconnect_IsProcessorOriginNotProofOfCpuDefect() =>
        Assert.Contains("defective_cpu", RuleAssertions.Present("whea_bus_interconnect", "hardware.whea.processor_mce").FalsePositiveContract);

    [Fact]
    public void WheaOutsideRebootWindow_RemainsSeparate()
    {
        var result = DiagnosticFixture.Load("whea_outside_reboot_window").Analyze();
        Assert.DoesNotContain(result.Incidents.Single(item => item.Category == IncidentCategory.Power).SourceEvents, DiagnosticFacts.IsWhea);
    }

    [Fact]
    public void StorageOutsideWindow_DoesNotCreateRepeatedTimeout() =>
        RuleAssertions.Absent("storage_timeout_outside_window", "storage.repeated_timeout_pattern");

    [Theory]
    [InlineData("application_crash")]
    [InlineData("audiodg_windows_module")]
    public void ReliabilityOriginalRecordIdentity_CorroboratesWithoutModuleOrProductExecutable(string scenario)
    {
        var fixture = DiagnosticFixture.Load(scenario);
        var source = fixture.Events[0];
        fixture.Events[0] = source with { Fields = new Dictionary<string, string>(source.Fields) { ["OriginalRecordId"] = "7", ["OriginalChannel"] = "Application" } };
        fixture.Events.Add(source with { Id = Guid.NewGuid(), TimestampUtc = source.TimestampUtc.AddSeconds(-1), SourceType = SourceType.Reliability, Channel = "Reliability", Process = null, Module = null,
            Fields = new Dictionary<string, string> { ["OriginalRecordId"] = "7", ["OriginalChannel"] = "Application", ["ProductName"] = "Synthetic Product" }, SourceReference = "synthetic:reliability:7" });
        var incident = Assert.Single(fixture.Analyze().Incidents);
        Assert.Equal(EvidenceStrength.Moderate, incident.EvidenceStrength);
        Assert.Contains(incident.Relations, item => item.Kind == RelationKind.DerivedFrom);
        Assert.Single(incident.Evidence.Where(item => item.Kind == EvidenceKind.Positive).Select(item => item.ObservationId).Distinct());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void MissingReportId_CrossRepresentationFallbackHasClosedTwoSecondCap(int seconds, bool same)
    {
        var fixture = DiagnosticFixture.Load("application_crash_with_wer");
        var first = fixture.Events[0];
        var fields = new Dictionary<string, string>(first.Fields);
        fields.Remove("ReportId");
        first = first with { Fields = fields };
        var second = fixture.Events[1] with { TimestampUtc = first.TimestampUtc.AddSeconds(seconds), ProcessId = first.ProcessId,
            Fields = new Dictionary<string, string>(fields) { ["EventName"] = "APPCRASH" } };
        Assert.Equal(same, DiagnosticFacts.SameOccurrence(first, second));
        Assert.False(DiagnosticFacts.SameOccurrence(first, second with { ProcessId = 999 }));
        Assert.False(DiagnosticFacts.SameOccurrence(first, first with { Id = Guid.NewGuid(), TimestampUtc = first.TimestampUtc.AddSeconds(1) }));
    }

    [Theory]
    [InlineData("whea_corrected")]
    [InlineData("tdr_without_driver")]
    [InlineData("storage_129")]
    [InlineData("service_single")]
    public void MissingStableIdentity_DoesNotCreateRecurringPattern(string fixtureName)
    {
        var fixture = DiagnosticFixture.Load(fixtureName);
        var source = fixture.Events.Last();
        var fields = new Dictionary<string, string>();
        if (DiagnosticFacts.IsTdr(source)) { fields["EventName"] = "LiveKernelEvent"; fields["P1"] = "141"; }
        source = source with { Fields = fields, Device = null };
        fixture.Events.Clear();
        fixture.Events.Add(source);
        fixture.Events.Add(source with { Id = Guid.NewGuid(), TimestampUtc = source.TimestampUtc.AddSeconds(60) });
        Assert.Empty(fixture.Analyze().Patterns);
    }

    [Theory]
    [InlineData("application_crash")]
    [InlineData("audiodg_third_party_apo")]
    [InlineData("nvidia_tdr_141")]
    [InlineData("whea_processor_cache")]
    [InlineData("storage_129")]
    [InlineData("service_single")]
    public void EveryRecurringFamily_DistinguishesExactRelatedAndCategory(string fixtureName)
    {
        var original = DiagnosticFixture.Load(fixtureName).Events.Last();
        NormalizedEvent Field(string name, string value) => original with { Fields = new Dictionary<string, string>(original.Fields) { [name] = value } };
        var (related, category) = DiagnosticFacts.Kind(original) switch
        {
            "graphics" => (Field("P1", "117"), Field("Vendor", "OtherVendor")),
            "whea" => (Field("ErrorType", "BusInterconnect"), Field("ApicId", "99")),
            "service" => (original with { EventId = 7034 }, Field("ServiceName", "OtherService")),
            "storage" => (original with { Provider = "Disk", EventId = 153 }, original with { Device = "other-disk" }),
            "audio" => (original with { Module = "other.dll" }, original with { Process = "Other.exe" }),
            _ => (original with { Module = "other.dll" }, original with { Process = "Other.exe" })
        };
        Assert.Equal(SignatureMatch.Exact, DiagnosticFacts.CompareSignatures(original, original));
        Assert.Equal(SignatureMatch.Related, DiagnosticFacts.CompareSignatures(original, related));
        Assert.Equal(DiagnosticFacts.Kind(original) == "audio" ? SignatureMatch.Unrelated : SignatureMatch.SameCategory,
            DiagnosticFacts.CompareSignatures(original, category));
    }

    [Theory]
    [InlineData("kernel41_only", "psu failure")]
    [InlineData("nvidia_tdr_141", "defective gpu")]
    [InlineData("whea_processor_cache", "replace cpu")]
    [InlineData("storage_153_single", "dying ssd")]
    [InlineData("pnp_219_benign", "broken usb device")]
    [InlineData("windows_fault_module", "ntdll.dll is the root cause")]
    [Trait("Category", "FalsePositive")]
    public void AssessmentLanguage_NeverTurnsNamedRecordIntoHardwareOrModuleDiagnosis(string fixtureName, string forbidden)
    {
        var language = new LocalizationService();
        foreach (var finding in DiagnosticFixture.Load(fixtureName).Analyze().Incidents.SelectMany(item => item.Findings))
        {
            Assert.Empty(finding.HypothesisKeys);
            var asserted = language.Get(finding.ObservedKey) + language.Get(finding.InterpretationKey) + string.Join(" ", finding.RecommendedActionKeys.Select(language.Get));
            Assert.DoesNotContain(forbidden, asserted, StringComparison.OrdinalIgnoreCase);
        }
    }
}
