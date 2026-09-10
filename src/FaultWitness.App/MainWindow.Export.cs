using System.Net;
using System.Globalization;
using Avalonia.Input.Platform;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Rules;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private bool exportSelectedOnly;
    private ExportFormat exportFormat;
    public string ExportPreview { get; private set; } = string.Empty;
    public const int PreviewCharacterLimit = 24_000;
    private ScanResult ExportResult => exportSelectedOnly && ViewModel.Selected is { } selected
        ? new ScanResult([selected.Incident], ViewModel.Result.Coverage, ViewModel.Result.StartedUtc, ViewModel.Result.FinishedUtc)
        : ViewModel.Result;
    private string SupportText() => ReportExporter.ToSupportMarkdown(ExportResult, ViewModel.Text, ViewModel.IsImported, ReleaseIdentity.Display, RuleCatalog.DatabaseVersion);
    private Control BuildExport()
    {
        if (!ViewModel.HasAnalysis) return Empty("NoAnalysis");
        var formats = new ComboBox { Name = "ExportFormat", ItemsSource = new[] { T("ExportSummary"), "HTML", "JSON", T("ExportBundle") }, SelectedIndex = (int)exportFormat, Width = 280 };
        var scope = new ComboBox { Name = "ExportScope", ItemsSource = ViewModel.Selected is null ? new[] { T("EntireAnalysis") } : new[] { T("EntireAnalysis"), T("SelectedIncident") }, SelectedIndex = exportSelectedOnly && ViewModel.Selected is not null ? 1 : 0, Width = 280 };
        var preview = new TextBox { Name = "ExportPreview", IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 320 };
        var previewNote = Muted(string.Empty);
        void RefreshPreview()
        {
            exportFormat = (ExportFormat)Math.Max(0, formats.SelectedIndex);
            exportSelectedOnly = scope.SelectedIndex == 1;
            ExportPreview = exportFormat == ExportFormat.Json ? ReportExporter.ToJson(ExportResult, new ExportPrivacyOptions()) : SupportText();
            preview.Text = ExportPreview.Length <= PreviewCharacterLimit ? ExportPreview : ExportPreview[..PreviewCharacterLimit];
            previewNote.Text = ExportPreview.Length > PreviewCharacterLimit ? T("PreviewExcerpt") : string.Empty;
        }
        formats.SelectionChanged += (_, _) => RefreshPreview(); scope.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPreview();
        return Scroll(Stack(Heading("Export", "ExportHelp"), Actions(Field("Format", formats), Field("Scope", scope)),
            Surface(Stack(Label(T("RedactionNotice")), Muted(T("BundleContents")))), Label(T("Preview"), TextRole.SectionTitle), previewNote, preview,
            Actions(AsyncButton("CopyForSupport", CopySupportAsync, "CopySupport"), AsyncButton("SaveExport", SaveExportAsync, "SaveExport"))));
    }
    public async Task CopySupportAsync()
    {
        if (Clipboard is null) { ViewModel.Notify("ClipboardUnavailable"); return; }
        await Clipboard.SetTextAsync(SupportText()).ConfigureAwait(true);
        ViewModel.Notify("SummaryCopied");
    }
    private async Task SaveExportAsync()
    {
        var extension = exportFormat switch { ExportFormat.Html => "html", ExportFormat.Json => "json", ExportFormat.Bundle => "zip", _ => "md" };
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = T("SaveExport"), SuggestedFileName = "FaultWitness-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "." + extension,
            DefaultExtension = extension, FileTypeChoices = [new FilePickerFileType(T("Export")) { Patterns = ["*." + extension] }]
        }).ConfigureAwait(true);
        if (file is null) return;
        if (exportFormat == ExportFormat.Bundle)
        {
            var path = file.TryGetLocalPath();
            if (path is null) { ViewModel.Notify("LocalDestinationRequired"); return; }
            await ReportExporter.CreateSupportBundleAsync(path, ExportResult, new ExportPrivacyOptions(), CancellationToken.None).ConfigureAwait(true);
        }
        else
        {
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            var output = exportFormat switch
            {
                ExportFormat.Json => ReportExporter.ToJson(ExportResult, new ExportPrivacyOptions()),
                ExportFormat.Html => "<!doctype html><html><head><meta charset='utf-8'><title>FaultWitness</title></head><body><pre style='white-space:pre-wrap'>" + WebUtility.HtmlEncode(SupportText()) + "</pre></body></html>",
                _ => SupportText()
            };
            await writer.WriteAsync(output).ConfigureAwait(true);
        }
        ViewModel.Notify("ExportSaved");
    }
}
