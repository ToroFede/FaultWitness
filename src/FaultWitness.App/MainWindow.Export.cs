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
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;

namespace FaultWitness.App;

public sealed partial class MainWindow
{
    private bool exportSelectedOnly;
    private ExportFormat exportFormat;
    private ExportSupportView? exportSupportView;
    private ExportSupportPresentation? exportSupportPresentation;
    public string ExportPreview { get; private set; } = string.Empty;
    public const int PreviewCharacterLimit = 24_000;
    private ScanResult ExportResult => exportSelectedOnly && ViewModel.Selected is { } selected
        ? new ScanResult([selected.Incident], ViewModel.Result.Coverage, ViewModel.Result.StartedUtc, ViewModel.Result.FinishedUtc)
        : ViewModel.Result;
    private string SupportText() => ReportExporter.ToSupportMarkdown(ExportResult, ViewModel.Text, ViewModel.IsImported, ReleaseIdentity.Display, RuleCatalog.DatabaseVersion);
    private ExportSupportView ExportSupportPage(bool refresh = true)
    {
        if (exportSupportView is null)
        {
            exportSupportPresentation = new ExportSupportPresentation();
            exportSupportView = new ExportSupportView();
            exportSupportView.OptionsChanged += ExportOptionsChanged;
            exportSupportView.CopyRequested += async () => await RunGuardedAsync(CopySupportAsync).ConfigureAwait(true);
            exportSupportView.SaveRequested += async () => await RunGuardedAsync(SaveExportAsync).ConfigureAwait(true);
        }
        if (refresh) RefreshExportPresentation();
        return exportSupportView;
    }

    private void ExportOptionsChanged(int formatIndex, int scopeIndex)
    {
        exportFormat = (ExportFormat)Math.Clamp(formatIndex, 0, Enum.GetValues<ExportFormat>().Length - 1);
        exportSelectedOnly = scopeIndex == 1;
        RefreshExportPresentation();
    }

    private void RefreshExportPresentation()
    {
        if (ViewModel.HasAnalysis)
            ExportPreview = exportFormat == ExportFormat.Json ? ReportExporter.ToJson(ExportResult, new ExportPrivacyOptions()) : SupportText();
        exportSupportPresentation?.Refresh(ViewModel, exportFormat, exportSelectedOnly, ExportPreview);
        exportSupportView?.Refresh(exportSupportPresentation!);
    }
    public async Task CopySupportAsync()
    {
        var origin = ViewModel.Page;
        ViewModel.ClearFeedback();
        var revision = ViewModel.FeedbackRevision;
        if (Clipboard is null) { ViewModel.Notify("ClipboardUnavailable", origin, revision); return; }
        await Clipboard.SetTextAsync(SupportText()).ConfigureAwait(true);
        ViewModel.Notify("SummaryCopied", origin, revision);
    }
    private async Task SaveExportAsync()
    {
        var origin = ViewModel.Page;
        ViewModel.ClearFeedback();
        var revision = ViewModel.FeedbackRevision;
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
            if (path is null) { ViewModel.Notify("LocalDestinationRequired", origin, revision); return; }
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
        ViewModel.Notify("ExportSaved", origin, revision);
    }
}
