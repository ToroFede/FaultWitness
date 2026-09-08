using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FaultWitness.Core;
using FaultWitness.Localization;

namespace FaultWitness.Export;

public sealed class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToMarkdown(ScanResult result, ExportPrivacyOptions privacy, LocalizationService? translation = null)
    {
        var language = translation ?? new LocalizationService();
        var text = new StringBuilder("# FaultWitness — " + language.Get("SupportTitle") + "\n\n");
        text.AppendLine(language.Get("DateRange") + ": " + result.StartedUtc.ToString("O") + " — " + result.FinishedUtc.ToString("O"));
        foreach (var incident in result.Incidents)
        {
            text.AppendLine("\n## " + language.Get("Category" + incident.Category) + " — " + incident.StartTimeUtc.ToLocalTime().ToString("g", language.Culture));
            text.AppendLine(language.Format("EvidenceValue", language.Get("Strength" + incident.EvidenceStrength)));
            foreach (var finding in incident.Findings.Where(static item => item.Disposition != FindingDisposition.Suppressed))
            {
                text.AppendLine("- " + language.Get("WhatHappened") + ": " + language.Get(finding.ObservedKey));
                text.AppendLine("- " + language.Get("Assessment") + ": " + language.Get(finding.InterpretationKey));
                text.AppendLine("- " + language.Get("CannotConclude") + ": " + language.Get(finding.NotEstablishedKey));
            }
            foreach (var evidence in incident.Evidence.DistinctBy(static item => (item.Kind, item.Family, item.LocalizationKey)))
            {
                var description = evidence.Kind == EvidenceKind.Unknown ? UnknownEvidenceText(evidence, result.Coverage, language) : language.Get(evidence.LocalizationKey);
                if (evidence.Kind == EvidenceKind.Positive && evidence.SourceEvent is { } record)
                {
                    var observed = incident.Findings.FirstOrDefault(item => item.Disposition != FindingDisposition.Suppressed);
                    description = evidence.ObservationId == incident.AnchorEvent.Id.ToString() && observed is not null
                        ? language.Get(observed.ObservedKey) : language.Format("ObservedSourceRecord", language.Get("SourceType" + record.SourceType), record.Provider);
                }
                text.AppendLine("- " + language.Get("Evidence" + evidence.Kind) + ": " + description);
            }
            var actions = incident.Findings.SelectMany(static item => item.RecommendedActionKeys).Distinct().ToArray();
            if (actions.Length > 0) text.AppendLine("- " + language.Get("BestNextStep") + ": " + language.Get(actions[0]));
        }
        text.AppendLine("\n## " + language.Get("SourceCoverage"));
        foreach (var source in result.Coverage)
            text.AppendLine("- " + source.SourceType + (source.Channel is null ? "" : "/" + source.Channel) + ": " + language.Get("Coverage" + source.State) + ". " + language.Get("CoverageHelp" + source.State));
        return Redact(text.ToString(), privacy);
    }

    private static string UnknownEvidenceText(Evidence evidence, IReadOnlyList<SourceCoverage> coverage, LocalizationService language)
    {
        var family = evidence.Family.Split('.', 2);
        if (!Enum.TryParse<SourceType>(family[0], out var type)) return language.Get(evidence.LocalizationKey);
        var channel = family.Length > 1 && family[1].Length > 0 ? family[1] : null;
        var key = type == SourceType.EventLog && channel is "System" or "Application" ? "Source" + channel : "SourceType" + type;
        var states = coverage.Where(item => item.SourceType == type && item.Channel == channel && item.State != CoverageState.Complete)
            .Select(item => language.Get("Coverage" + item.State)).Distinct().ToArray();
        return language.Get(key) + " — " + (states.Length == 0 ? language.Get("IncidentCoverageUnknown") : string.Join(" / ", states)) +
            (evidence.LocalizationKey == "evidence.source.unknown" ? "" : ". " + language.Get(evidence.LocalizationKey));
    }

    public static string ToSupportMarkdown(ScanResult result, LocalizationService language, bool imported, string appVersion, string ruleVersion) =>
        ToMarkdown(result, new ExportPrivacyOptions(), language) + "\n" + language.Get("System") + ": " +
        language.Get(imported ? "ImportedData" : "LocalSystem") + " (Windows)\nFaultWitness " + appVersion + "\n" + language.Get("RuleDatabase") + ": " + ruleVersion + "\n";

    public static string ToJson(ScanResult result, ExportPrivacyOptions privacy)
    {
        var export = new
        {
            result.StartedUtc,
            result.FinishedUtc,
            result.Coverage,
            Incidents = result.Incidents.Select(incident => new
            {
                incident.Id,
                incident.StartTimeUtc,
                incident.EndTimeUtc,
                incident.Category,
                incident.Severity,
                EvidenceStrength = incident.EvidenceStrength.ToString(),
                Signature = privacy.RedactPersonalData ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(incident.Signature))) : incident.Signature,
                Findings = incident.Findings.Select(finding => new { finding.RuleId, Strength = finding.Strength.ToString(), finding.Disposition, finding.ObservedKey, finding.InterpretationKey, finding.NotEstablishedKey, finding.HypothesisKeys, finding.FalsePositiveContract }),
                Evidence = incident.Evidence.Select(evidence => new { evidence.Kind, evidence.Family, evidence.LocalizationKey, evidence.ObservationId }),
                incident.Relations,
                Events = incident.SourceEvents.Select(source => SanitizeEvent(source, privacy)).Select(source => new
                {
                    source.TimestampUtc,
                    source.Channel,
                    source.Provider,
                    source.EventId,
                    source.EventVersion,
                    source.Process,
                    source.Module,
                    source.Device,
                    source.Fields,
                    source.SourceReference,
                    RawXml = privacy.IncludeRawXml ? source.RawData : null
                })
            })
        };
        var json = JsonSerializer.Serialize(export, JsonOptions);
        return json;
    }

    public static string ToHtml(ScanResult result, ExportPrivacyOptions privacy)
    {
        var markdown = ToMarkdown(result, privacy);
        var safe = WebUtility.HtmlEncode(markdown).Replace("\n", "<br/>", StringComparison.Ordinal);
        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>FaultWitness Report</title></head><body><main><pre>" + safe + "</pre></main></body></html>";
    }

    public static async Task CreateSupportBundleAsync(string destination, ScanResult result, ExportPrivacyOptions privacy, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(destination);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, false);
        var report = archive.CreateEntry("support-summary.md", CompressionLevel.Optimal);
        await using (var writer = new StreamWriter(report.Open(), new UTF8Encoding(false)))
            await writer.WriteAsync(ToMarkdown(result, privacy).AsMemory(), cancellationToken).ConfigureAwait(false);
        var events = archive.CreateEntry("events.json", CompressionLevel.Optimal);
        await using var eventWriter = new StreamWriter(events.Open(), new UTF8Encoding(false));
        var safeEvents = result.Incidents.SelectMany(static incident => incident.SourceEvents).DistinctBy(static item => item.Id)
            .Select(source => SanitizeEvent(source, privacy));
        await eventWriter.WriteAsync(JsonSerializer.Serialize(safeEvents, JsonOptions).AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static NormalizedEvent SanitizeEvent(NormalizedEvent source, ExportPrivacyOptions privacy)
    {
        string Clean(string value) => Redact(value, privacy);
        var fields = source.Fields.ToDictionary(static pair => pair.Key, pair =>
            privacy.RedactPersonalData && (pair.Key.Contains("Serial", StringComparison.OrdinalIgnoreCase) ||
            pair.Key.Contains("Account", StringComparison.OrdinalIgnoreCase) || pair.Key.Contains("User", StringComparison.OrdinalIgnoreCase))
                ? "<redacted>" : pair.Key is "AppVersion" or "ModuleVersion" or "ExceptionCode" or "BugcheckCode" ||
                    (source.Field("EventName") == "APPCRASH" && pair.Key is "P2" or "P5") ||
                    (source.Field("EventType") == "APPCRASH" && pair.Key is "Sig[1].Value" or "Sig[4].Value")
                    ? pair.Value : Clean(pair.Value), StringComparer.OrdinalIgnoreCase);
        return source with
        {
            Process = source.Process is null ? null : Clean(source.Process),
            Module = source.Module is null ? null : Clean(source.Module),
            Device = source.Device is null ? null : Clean(source.Device),
            Fields = fields,
            SourceReference = Clean(source.SourceReference),
            RawData = privacy.IncludeRawXml && source.RawData is not null ? Clean(source.RawData) : null
        };
    }

    private static string Redact(string input, ExportPrivacyOptions privacy)
    {
        if (!privacy.RedactPersonalData) return input;
        var output = Regex.Replace(input, "(?i)([A-Z]:\\\\Users\\\\)[^\\\\\"\\r\\n]+", "$1<redacted>");
        output = Regex.Replace(output, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "<redacted-ip>");
        output = output.Replace(Environment.MachineName, "<redacted-computer>", StringComparison.OrdinalIgnoreCase);
        return output;
    }
}
