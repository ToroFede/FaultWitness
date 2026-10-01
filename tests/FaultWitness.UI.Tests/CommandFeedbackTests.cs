using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Rules;
using FaultWitness.Storage;

#pragma warning disable CA2000 // MainWindow owns its view model; synthetic storage owns its stream.

namespace FaultWitness.UI.Tests;

[Trait("Suite", "CommandFeedback")]
public sealed class CommandFeedbackTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidInputIsLocalKeepsFocusAndDoesNotInvokeAnalysis(bool around)
    {
        var services = new TestServices();
        var window = Open(services);
        try
        {
            window.ViewModel.OpenAnalyze(around ? AnalysisMode.Around : AnalysisMode.Recent);
            var page = Current<AnalyzeView>(window);
            if (around) page.FindControl<DatePicker>("AroundDate")!.SelectedDate = DateTimeOffset.Now.AddDays(2);
            else
            {
                page.FindControl<ComboBox>("PeriodSelector")!.SelectedIndex = (int)AnalysisPeriod.Custom;
                page.FindControl<DatePicker>("FromDate")!.SelectedDate = DateTimeOffset.Now.AddDays(2);
            }
            var action = page.FindControl<Button>(around ? "RunAroundAnalysis" : "RunAnalysis")!;
            action.Focus();
            Click(action);
            Assert.Equal("InvalidTimeRange", window.ViewModel.StatusKey);
            Assert.Equal(0, services.AnalysisCalls);
            Assert.Equal(0, services.Saved);
            Assert.True(action.IsFocused);
            AssertFeedback(window, "Analyze");
            window.UpdateLayout();
            Assert.True(page.FindControl<TextBlock>("AnalyzeFeedback")!.TranslatePoint(default, page)!.Value.Y < action.TranslatePoint(default, page)!.Value.Y);
            if (around) page.FindControl<DatePicker>("AroundDate")!.SelectedDate = DateTimeOffset.Now.AddDays(-1);
            else page.FindControl<DatePicker>("FromDate")!.SelectedDate = DateTimeOffset.Now.AddDays(-1);
            Assert.Equal("Ready", window.ViewModel.StatusKey);
            Assert.False(page.FindControl<StackPanel>("AnalyzeFeedbackRegion")!.IsVisible);
            Click(action);
            await WaitFor(() => !window.ViewModel.IsBusy && window.ViewModel.HasAnalysis);
            Assert.Equal(1, services.AnalysisCalls);
            Assert.Equal(1, services.Saved);
            Assert.Same(services.Result, window.ViewModel.Result);
            Assert.Equal("AnalysisComplete", window.ViewModel.StatusKey);
            Assert.Equal(AppPage.Home, window.ViewModel.Page);
            AssertSingleLiveOwner(window, "StatusText");
            Assert.Equal(around ? TimeSpan.FromMinutes(10) : TimeSpan.FromDays(2).Subtract(TimeSpan.FromSeconds(1)), services.To - services.From);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RunningNavigationModeSwitchAndCancellationUseOneOwnerAndKeepResults()
    {
        var services = new TestServices { WaitForCancellation = true };
        var window = Open(services);
        var previous = SyntheticResults.Create(2);
        Task? running = null;
        try
        {
            var vm = window.ViewModel;
            vm.SetResult(previous);
            vm.OpenAnalyze(AnalysisMode.Recent);
            running = vm.AnalyzeAsync();
            AssertFeedback(window, "Analyze");
            var page = Current<AnalyzeView>(window);
            Assert.False(page.FindControl<Button>("RunAnalysis")!.IsEnabled, "Run must be disabled while busy");
            Assert.True(page.FindControl<Button>("CancelAnalyzeLocal")!.IsVisible);
            Assert.True(page.FindControl<ProgressBar>("AnalyzeLocalProgress")!.IsIndeterminate);
            await vm.AnalyzeAsync();
            Assert.Equal(1, services.AnalysisCalls);
            vm.Navigate(AppPage.Export);
            Assert.False(vm.HasLocalFeedback, "Feedback must not belong to the new page or mode");
            AssertSingleLiveOwner(window, "StatusText");
            Assert.False(Current<ExportSupportView>(window).FindControl<StackPanel>("ExportFeedbackRegion")!.IsVisible, "Export must not show Analyze feedback");
            Assert.True(window.FindControl<Button>("CancelAnalysis")!.IsVisible);
            vm.OpenAnalyze(AnalysisMode.Around);
            AssertSingleLiveOwner(window, "StatusText");
            Assert.False(Current<AnalyzeView>(window).FindControl<StackPanel>("AnalyzeFeedbackRegion")!.IsVisible, "Around must not show Recent feedback");
            vm.OpenAnalyze(AnalysisMode.Recent);
            Assert.Same(page, Current<AnalyzeView>(window));
            AssertFeedback(window, "Analyze");
            Click(page.FindControl<Button>("CancelAnalyzeLocal")!);
            Assert.Equal("Cancelling", vm.StatusKey);
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("AnalysisCancelled", vm.StatusKey);
            Assert.Same(previous, vm.Result);
            Assert.Equal(0, services.Saved);
            Assert.False(page.FindControl<Button>("CancelAnalyzeLocal")!.IsVisible, "Cancel must disappear after cancellation");
            Assert.True(page.FindControl<Button>("RunAnalysis")!.IsEnabled);
            AssertFeedback(window, "Analyze");
            vm.Navigate(AppPage.Settings);
            vm.OpenAnalyze(AnalysisMode.Recent);
            Assert.Equal("Ready", vm.StatusKey);
            Assert.False(page.FindControl<StackPanel>("AnalyzeFeedbackRegion")!.IsVisible);
        }
        finally
        {
            window.ViewModel.Cancel();
            if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HistoryLoadFailureIsNotEmptyAndRetryPreservesQueryAndSelection()
    {
        var services = new TestServices { FailHistoryLoad = true };
        var window = Open(services);
        try
        {
            // Exercise Home's actual event path, including navigation before load.
            window.ViewModel.SetResult(SyntheticResults.Create(2));
            Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "OpenHistory"));
            await WaitFor(() => !window.ViewModel.IsBusy);
            Assert.Equal(AppPage.History, window.ViewModel.Page);
            var page = Current<HistoryView>(window);
            Assert.Equal("HistoryError", window.ViewModel.StatusKey);
            Assert.False(page.FindControl<Border>("HistoryEmpty")!.IsVisible);
            AssertFeedback(window, "History");
            Assert.True(page.FindControl<Button>("RetryHistory")!.IsVisible);
            Assert.Equal(1, services.HistoryLoads);
            services.FailHistoryLoad = false;
            Click(page.FindControl<Button>("RetryHistory")!);
            await WaitFor(() => !window.ViewModel.IsBusy);
            Assert.Equal(2, services.HistoryLoads);
            Assert.True(page.FindControl<Border>("HistoryEmpty")!.IsVisible);
            Assert.Equal("Ready", window.ViewModel.StatusKey);
            services.History = [StoredHistory()];
            await window.ViewModel.RefreshHistoryAsync();
            var selected = window.ViewModel.SelectedHistory;
            Assert.NotNull(selected);
            Assert.True(page.FindControl<ListBox>("HistoryList")!.IsVisible);
            services.FailHistoryLoad = true;
            await window.ViewModel.RefreshHistoryAsync();
            Assert.Same(selected, window.ViewModel.SelectedHistory);
            Assert.Single(window.ViewModel.History);
            Assert.False(page.FindControl<Border>("HistoryEmpty")!.IsVisible);
            AssertFeedback(window, "History");
            Assert.Equal(0, services.Cleared);
            Assert.Equal(0, services.Saved);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task HistoryLoadingAndExistingCancellationClassificationDoNotClaimEmpty()
    {
        var services = new TestServices { WaitForHistoryCancellation = true };
        var window = Open(services);
        Task? loading = null;
        try
        {
            window.ViewModel.Navigate(AppPage.History);
            loading = window.ViewModel.RefreshHistoryAsync();
            Assert.False(Current<HistoryView>(window).FindControl<Border>("HistoryEmpty")!.IsVisible);
            AssertFeedback(window, "History");
            window.ViewModel.Cancel();
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("HistoryError", window.ViewModel.StatusKey); // Existing catch-all classification is preserved.
            Assert.False(Current<HistoryView>(window).FindControl<Border>("HistoryEmpty")!.IsVisible);
        }
        finally
        {
            window.ViewModel.Cancel();
            if (loading is not null) await loading.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task SaveUsesExistingExporterAndResultClassificationBesideCommands(string outcome)
    {
        var window = Open(new TestServices());
        using var stream = new MemoryStream();
        try
        {
            var vm = window.ViewModel;
            vm.SetResult(SyntheticResults.Create(2));
            vm.Navigate(AppPage.Export);
            Current<ExportSupportView>(window).FindControl<ComboBox>("ExportFormat")!.SelectedIndex = (int)ExportFormat.Json;
            var file = DispatchProxy.Create<IStorageFile, FeedbackStorageProxy>();
            ((FeedbackStorageProxy)file).InvokeMethod = (method, _) => method.Name == "OpenWriteAsync"
                ? outcome == "failure" ? Task.FromException<Stream>(new IOException("synthetic export error")) : Task.FromResult<Stream>(stream)
                : method.Name == "Dispose" ? null : throw new NotSupportedException(method.Name);
            var storage = DispatchProxy.Create<IStorageProvider, FeedbackStorageProxy>();
            ((FeedbackStorageProxy)storage).InvokeMethod = (method, _) => method.Name == "SaveFilePickerAsync"
                ? Task.FromResult(outcome == "cancel" ? null : file) : throw new NotSupportedException(method.Name);
            typeof(TopLevel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(field => field.FieldType == typeof(IStorageProvider)).SetValue(window, storage);
            vm.Fail("OperationError", new UnauthorizedAccessException("old"));
            var action = Current<ExportSupportView>(window).FindControl<Button>("SaveExport")!;
            action.Focus();
            Click(action);
            await WaitFor(() => outcome == "cancel" ? vm.StatusKey == "Ready" : vm.StatusKey == (outcome == "failure" ? "OperationError" : "ExportSaved"));
            Assert.True(action.IsEnabled);
            Assert.True(action.IsFocused);
            if (outcome == "cancel") Assert.False(vm.HasVisibleStatus);
            else AssertFeedback(window, "Export");
            if (outcome == "failure") Assert.Equal("IOException", vm.TechnicalError);
            else Assert.Empty(vm.TechnicalError);
            if (outcome == "success")
            {
                var actual = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                Assert.Equal(ReportExporter.ToJson(vm.Result, new ExportPrivacyOptions()), actual);
                Assert.DoesNotContain("synthetic-private", actual, StringComparison.Ordinal);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public async Task CopyFeedbackRefreshesLocaleThemeAndReplacesErrorWithoutChangingPayload(string locale)
    {
        var window = Open(new TestServices());
        try
        {
            var vm = window.ViewModel;
            vm.SetResult(SyntheticResults.Create(2));
            vm.Navigate(AppPage.Export);
            var page = Current<ExportSupportView>(window);
            vm.Fail("OperationError", new IOException("old"));
            vm.ChangeSettings(vm.Settings with { Language = locale, Theme = AppTheme.Dark });
            Assert.Same(page, Current<ExportSupportView>(window));
            AssertFeedback(window, "Export");
            Click(page.FindControl<Button>("CopySupport")!);
            await WaitFor(() => vm.StatusKey == "SummaryCopied");
            Assert.Empty(vm.TechnicalError);
            AssertFeedback(window, "Export");
            Assert.Equal(ReportExporter.ToSupportMarkdown(vm.Result, vm.Text, vm.IsImported, ReleaseIdentity.Display, RuleCatalog.DatabaseVersion), await window.Clipboard!.TryGetTextAsync());
            vm.ChangeSettings(vm.Settings with { Theme = AppTheme.Light });
            AssertFeedback(window, "Export");
            vm.Navigate(AppPage.History);
            Assert.Equal("Ready", vm.StatusKey);
            vm.Navigate(AppPage.Export);
            Assert.False(page.FindControl<StackPanel>("ExportFeedbackRegion")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ModeChangeAndNewOperationReplaceOldValidationAndTechnicalDetails()
    {
        var services = new TestServices { FailAnalysis = true };
        var window = Open(services);
        try
        {
            var vm = window.ViewModel;
            vm.OpenAnalyze(AnalysisMode.Recent);
            await vm.AnalyzeAsync();
            Assert.Equal("AnalysisError", vm.StatusKey);
            vm.Period = AnalysisPeriod.Custom;
            vm.CustomFrom = DateTimeOffset.Now.AddDays(2);
            await vm.AnalyzeAsync();
            Assert.Equal("InvalidTimeRange", vm.StatusKey);
            Assert.Empty(vm.TechnicalError);
            Assert.Equal(1, services.AnalysisCalls);
            vm.OpenAnalyze(AnalysisMode.Around);
            Assert.Equal("Ready", vm.StatusKey);
            Assert.False(vm.HasLocalFeedback, "Feedback must not belong to the new page or mode");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DelayedExportFeedbackKeepsItsOriginAndCannotReplaceANewerCommand(bool fail, bool newerCommand)
    {
        var window = Open(new TestServices());
        using var stream = new MemoryStream();
        var picker = new TaskCompletionSource<IStorageFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var vm = window.ViewModel;
            vm.SetResult(SyntheticResults.Create(2));
            vm.Navigate(AppPage.Export);
            var file = DispatchProxy.Create<IStorageFile, FeedbackStorageProxy>();
            ((FeedbackStorageProxy)file).InvokeMethod = (method, _) => method.Name == "OpenWriteAsync"
                ? fail ? Task.FromException<Stream>(new IOException("delayed synthetic error")) : Task.FromResult<Stream>(stream)
                : method.Name == "Dispose" ? null : throw new NotSupportedException(method.Name);
            var storage = DispatchProxy.Create<IStorageProvider, FeedbackStorageProxy>();
            ((FeedbackStorageProxy)storage).InvokeMethod = (method, _) => method.Name == "SaveFilePickerAsync" ? picker.Task : throw new NotSupportedException(method.Name);
            typeof(TopLevel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(field => field.FieldType == typeof(IStorageProvider)).SetValue(window, storage);
            var guarded = typeof(MainWindow).GetMethod("RunGuardedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var save = typeof(MainWindow).GetMethod("SaveExportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = (Task)guarded.Invoke(window, [new Func<Task>(() => (Task)save.Invoke(window, null)!)])!;
            if (newerCommand) await window.CopySupportAsync();
            else vm.Navigate(AppPage.History);
            picker.SetResult(file);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            if (newerCommand)
            {
                Assert.Equal("SummaryCopied", vm.StatusKey);
                Assert.Empty(vm.TechnicalError);
                AssertFeedback(window, "Export");
            }
            else
            {
                Assert.Equal(fail ? "OperationError" : "ExportSaved", vm.StatusKey);
                Assert.False(vm.HasVisibleStatus);
                Assert.False(vm.HasLocalFeedback);
                Assert.False(Current<HistoryView>(window).FindControl<StackPanel>("HistoryFeedbackRegion")!.IsVisible);
            }
        }
        finally { picker.TrySetResult(null); window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardOrderAndValidationAssociationSurviveFeedbackUpdates()
    {
        var window = Open(new TestServices { WaitForCancellation = true });
        Task? running = null;
        try
        {
            var vm = window.ViewModel;
            vm.OpenAnalyze(AnalysisMode.Recent);
            var page = Current<AnalyzeView>(window);
            vm.Period = AnalysisPeriod.Custom;
            vm.CustomFrom = DateTimeOffset.Now.AddDays(2);
            await vm.AnalyzeAsync();
            Assert.Equal(vm.StatusText, AutomationProperties.GetHelpText(page.FindControl<DatePicker>("FromDate")!));
            vm.ChangeSettings(vm.Settings with { Language = "de", Theme = AppTheme.Dark });
            AssertFeedback(window, "Analyze");
            Assert.Equal(vm.StatusText, AutomationProperties.GetHelpText(page.FindControl<DatePicker>("FromDate")!));
            vm.Period = AnalysisPeriod.Week;
            running = vm.AnalyzeAsync();
            Current<AnalyzeView>(window);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(page.FindControl<DatePicker>("FromDate")!)));
            var advanced = page.FindControl<Expander>("AdvancedSources")!;
            var header = advanced.GetVisualDescendants().OfType<ToggleButton>().First();
            Assert.True(header.Focus());
            Tab(window);
            Assert.True(page.FindControl<Button>("CancelAnalyzeLocal")!.IsFocused);
            vm.Cancel();
            await running;
            vm.SetResult(SyntheticResults.Create(2));
            vm.Navigate(AppPage.Export);
            var export = Current<ExportSupportView>(window);
            vm.Fail("OperationError", new IOException("synthetic"));
            Assert.True(export.FindControl<Button>("CopySupport")!.Focus());
            Tab(window);
            Assert.True(export.FindControl<Button>("SaveExport")!.IsFocused);
        }
        finally
        {
            window.ViewModel.Cancel();
            if (running is not null) await running.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    private static void Tab(MainWindow window)
    {
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaTheory]
    [InlineData(600)][InlineData(640)][InlineData(641)][InlineData(1008)][InlineData(1280)][InlineData(1920)]
    public void ShellStatusSharesPageMeasureAndRemainsGlobalForOtherContexts(int width)
    {
        var window = Open(new TestServices(), width);
        try
        {
            window.ViewModel.Notify("AnalysisComplete");
            window.UpdateLayout();
            var page = window.FindControl<ContentControl>("PageHost")!;
            var status = window.FindControl<Grid>("StatusArea")!;
            Assert.Equal(page.Bounds.X, status.Bounds.X, 2);
            Assert.Equal(page.Bounds.Width, status.Bounds.Width, 2);
            AssertSingleLiveOwner(window, "StatusText");
        }
        finally { window.Close(); }
    }

    internal static MainWindow Open(TestServices services, int width = 1280)
    {
        var window = new MainWindow(new MainViewModel(services));
        VisualRenderGeometry.ShowAtRequestedGeometry(window, width, 900);
        return window;
    }
    internal static T Current<T>(MainWindow window) where T : Control
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return Assert.IsType<T>(window.FindControl<ContentControl>("PageHost")!.Content);
    }
    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static StoredScan StoredHistory()
    {
        var time = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        return new StoredScan("feedback-history", time, time.AddMinutes(1), "0.9.1", new ScanHistoryMetadata("recent", time.AddDays(-7), time, 15, 1, 0, 0, "SourceSystem=Complete"),
            [new StoredIncident("feedback-incident", time, "Graphics", "High", "synthetic-signature", "{}")]);
    }
    internal static void AssertFeedback(MainWindow window, string prefix)
    {
        var page = (Control)window.FindControl<ContentControl>("PageHost")!.Content!;
        Assert.True(page.FindControl<StackPanel>(prefix + "FeedbackRegion")!.IsVisible);
        var text = page.FindControl<TextBlock>(prefix + "Feedback")!;
        Assert.Equal(window.ViewModel.StatusText, text.Text);
        Assert.Equal(text.Text, AutomationProperties.GetName(text));
        Assert.False(window.FindControl<Grid>("StatusArea")!.IsVisible);
        AssertSingleLiveOwner(window, prefix + "Feedback");
    }
    internal static void AssertSingleLiveOwner(MainWindow window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var owners = window.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible && AutomationProperties.GetLiveSetting(control) != AutomationLiveSetting.Off).ToArray();
        Assert.Equal(name, Assert.Single(owners).Name);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(owners[0]));
    }
    internal static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.True(predicate());
    }
}

public class FeedbackStorageProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } = (_, _) => throw new NotSupportedException();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args);
}

#pragma warning restore CA2000
