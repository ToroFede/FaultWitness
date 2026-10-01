using System.Text.Json;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Storage;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private async Task CopyHistoryAsync()
    {
        if (Clipboard is null || ViewModel.SelectedHistory is not { } row) { ViewModel.Notify("ClipboardUnavailable"); return; }
        await Clipboard.SetTextAsync(HistorySummaryText(row)).ConfigureAwait(true);
        ViewModel.Notify("SummaryCopied");
    }

    private async Task SaveHistoryAsync()
    {
        if (ViewModel.SelectedHistory is not { } row) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = T("SaveExport"), SuggestedFileName = "FaultWitness-history.md", DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType(T("ExportSummary")) { Patterns = ["*.md"] }]
        }).ConfigureAwait(true);
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        await writer.WriteAsync(HistorySummaryText(row)).ConfigureAwait(true);
        ViewModel.Notify("ExportSaved");
    }

    private string HistorySummaryText(HistoryRow row)
    {
        var scan = row.Scan;
        return $"FaultWitness\n{row.Title}\n{row.Timestamp}\n{row.Period}\n{row.Counts}\nRules {scan.RulesVersion}\n" +
            string.Join("\n", scan.Incidents.Select(item => $"{item.OccurredUtc:O} | {item.Category} | {item.Severity} | {SavedChangeSummary(item)}"));
    }

    private string SavedChangeSummary(StoredIncident incident)
    {
        try
        {
            using var document = JsonDocument.Parse(incident.SummaryJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ChangeContext", out var context) || context.ValueKind == JsonValueKind.Null)
                return string.Empty;
            var savedContext = context.Deserialize<ChangeHistoryContext>();
            var changes = root.TryGetProperty("RelatedChanges", out var related) && related.ValueKind == JsonValueKind.Array
                ? related.Deserialize<RelatedSystemChange[]>() ?? [] : [];
            return ChangePresentation.ToMarkdown(savedContext, changes, ViewModel.Text, new ExportPrivacyOptions());
        }
        catch (JsonException) { return string.Empty; }
    }

    private async Task BrowseImportsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Browse"), AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(T("DiagnosticFiles")) { Patterns = ["*.evtx", "*.wer", "*.zip"] }]
        }).ConfigureAwait(true);
        ViewModel.AddImports(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    private async Task ConfirmClearAsync()
    {
        var dialog = new ClearDataConfirmationWindow();
        dialog.Prepare(ViewModel.Text, this);
        if (await dialog.ShowDialog<bool>(this).ConfigureAwait(true)) await ViewModel.ClearDataAsync().ConfigureAwait(true);
    }
}
