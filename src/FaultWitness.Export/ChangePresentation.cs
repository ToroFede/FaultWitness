using System.Text;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.Export;

/// <summary>Compact, non-causal presentation shared by live and saved summaries.</summary>
public static class ChangePresentation
{
    public const int MaxChanges = 12;
    public const string Disclaimer = "Temporal proximity does not establish causation.";

    public static string ToMarkdown(Incident incident, LocalizationService text, ExportPrivacyOptions privacy) =>
        ToMarkdown(incident.ChangeContext, incident.RelatedChanges, text, privacy);

    public static string ToMarkdown(ChangeHistoryContext? context, IReadOnlyList<RelatedSystemChange> relatedChanges,
        LocalizationService text, ExportPrivacyOptions privacy)
    {
        var output = new StringBuilder("### " + text.Get("ChangesNearFirst") + "\n");
        if (context is null) return output.AppendLine(text.Get("ChangesNotExamined")).ToString();
        output.AppendLine(text.Format("FirstObservedValue", context.FirstObservedUtc.ToString("O"),
            text.Get("FirstObservedBasis" + context.Basis)));
        output.AppendLine(text.Get(context.IsRecurring ? "ChangesRecurring" : "ChangesNotRecurring"));
        if (!relatedChanges.Any(item => item.Relevance != ContextualRelevance.Low)) output.AppendLine(text.Get("NoRelevantChanges"));
        foreach (var related in relatedChanges.OrderBy(item => item.Relevance).ThenBy(item => item.OffsetFromFirstObservation.Duration()))
            output.AppendLine("- " + RowText(related, text) + " · " + Details(related, text));
        if (context.TotalChangeCount > relatedChanges.Count)
            output.AppendLine(text.Format("ChangesSummaryLimited", relatedChanges.Count, context.TotalChangeCount));
        if (context.Coverage.Count == 0 || context.Coverage.Any(item => item.State != CoverageState.Complete))
            output.AppendLine(text.Get("ChangesCoverageIncomplete"));
        foreach (var coverage in context.Coverage) output.AppendLine("- " + CoverageText(coverage, text));
        output.AppendLine(text.Get("ChangesDisclaimer"));
        return ReportExporter.Redact(output.ToString(), privacy);
    }

    public static string RowText(RelatedSystemChange related, LocalizationService text)
    {
        var change = SystemChangePrivacy.Sanitize(related.Change);
        return text.Get("ChangeSubsystem" + change.Subsystem) + " · " + text.Get("ChangeCategory" + change.Category) +
            " · " + change.Subject + (string.IsNullOrWhiteSpace(change.Vendor) ? "" : " · " + change.Vendor) +
            (change.NewValue is null ? "" : " · " + text.Format("ChangeVersion", change.NewValue)) +
            (change.PreviousValue is null ? "" : " · " + text.Format("ChangePreviousVersion", change.PreviousValue)) +
            " · " + text.Get("ChangeTiming" + related.Timing) + " · " + change.TimestampUtc.ToLocalTime().ToString("g", text.Culture) +
            " · " + text.Get("ChangeRelevance" + related.Relevance);
    }

    public static string Details(RelatedSystemChange related, LocalizationService text)
    {
        var change = SystemChangePrivacy.Sanitize(related.Change);
        return change.TimestampUtc.ToString("O") + " · " + text.Format("ChangeOffset", related.OffsetFromFirstObservation) +
            " · " + text.Get("ChangeSource") + ": " + text.Get(change.Source) + " · " + change.SourceReference + " · " + text.Get(related.ReasonKey) +
            " · " + text.Get("ChangeQuality" + change.Quality);
    }

    public static string CoverageText(SourceCoverage coverage, LocalizationService text) =>
        text.Get(coverage.Channel ?? "ChangeSourceLocalHistory") + ": " + text.Get("Coverage" + coverage.State) +
        " · " + text.Get(coverage.DetailKey) + " · " + coverage.ExaminedFromUtc?.ToString("O") + " — " + coverage.ExaminedToUtc?.ToString("O");

    public static object ToJsonModel(Incident incident, ExportPrivacyOptions privacy) => new
    {
        Context = incident.ChangeContext,
        Changes = incident.RelatedChanges.Select(related =>
        {
            var change = SystemChangePrivacy.Sanitize(related.Change);
            string? Clean(string? value) => value is null ? null : ReportExporter.Redact(value, privacy);
            return new
            {
                change.TimestampUtc, Category = change.Category.ToString(), Subsystem = change.Subsystem.ToString(),
                Source = Clean(change.Source), Subject = Clean(change.Subject), SourceReference = Clean(change.SourceReference),
                PreviousValue = Clean(change.PreviousValue), NewValue = Clean(change.NewValue), Vendor = Clean(change.Vendor),
                change.Quality, change.UpdateIdentity, Relevance = related.Relevance.ToString(), Timing = related.Timing.ToString(),
                related.OffsetFromFirstObservation, related.ReasonKey
            };
        }),
        Disclaimer
    };
}
