using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Rules;

namespace FaultWitness.App;

/// <summary>Readable evidence, without changing votes, source records or diagnostic conclusions.</summary>
public static class EvidencePresentation
{
    private static readonly IReadOnlyList<IDiagnosticRule> Rules = RuleCatalog.CreateDefault().ToArray();
    public static string FriendlyEvent(NormalizedEvent item, LocalizationService text)
    {
        if (DiagnosticFacts.IsVendor(item)) return text.Get("DisplayDriverEvent");
        var rule = Rules.FirstOrDefault(rule => rule.AppliesTo(item));
        return rule is null ? text.Format("ObservedSourceRecord", text.Get("SourceType" + item.SourceType), item.Provider)
            : text.Get("rule." + rule.RuleId + ".observed");
    }
    public static string Describe(Evidence evidence, IReadOnlyList<SourceCoverage> coverage, LocalizationService text)
    {
        if (evidence.Kind == EvidenceKind.Positive && evidence.SourceEvent is { } record)
        {
            var identity = string.Join(" · ", new[] { record.Process, record.Module }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return FriendlyEvent(record, text) + " · " + text.Get("SourceType" + record.SourceType) + " · " +
                record.TimestampUtc.ToLocalTime().ToString("T", text.Culture) + (identity.Length == 0 ? "" : " · " + identity);
        }
        if (evidence.Kind != EvidenceKind.Unknown) return text.Get(evidence.LocalizationKey);
        var family = evidence.Family.Split('.', 2);
        if (!Enum.TryParse<SourceType>(family[0], out var sourceType)) return text.Get(evidence.LocalizationKey) + " · " + evidence.Family;
        var channel = family.Length > 1 && family[1].Length > 0 ? family[1] : null;
        var matching = coverage.Where(source => source.SourceType == sourceType && string.Equals(source.Channel, channel, StringComparison.OrdinalIgnoreCase)).ToArray();
        var name = text.Get(PresentationPolicy.SourceKey(new(sourceType, CoverageState.Unavailable, null, null, "", channel)));
        var states = matching.Where(source => source.State != CoverageState.Complete).Select(source => text.Get("Coverage" + source.State)).Distinct().ToArray();
        var status = states.Length == 0 ? text.Get("IncidentCoverageUnknown") : string.Join(" / ", states);
        return name + " — " + status + (evidence.LocalizationKey == "evidence.source.unknown" ? "" : ". " + text.Get(evidence.LocalizationKey));
    }
}
