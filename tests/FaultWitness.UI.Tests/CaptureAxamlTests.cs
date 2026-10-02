using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class CaptureAxamlTests
{
    private static readonly string[] Regions = ["CapturePurpose", "CaptureCurrentRegion", "CaptureTargetRegion", "CapturePolicyRegion", "CaptureSafetyRegion", "CapturePreviewRegion", "CaptureApplyRegion", "CaptureResultRegion", "CaptureRestoreRegion", "CaptureJournalRegion", "CaptureTechnicalRegion"];

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public void SingleTitleAndReadingHierarchy_AreAuthoredOnce(bool fallback)
    {
        var service = new Service();
        var flow = fallback ? null : new CaptureWorkflow(service, new Journal());
        var window = Open(flow);
        try
        {
            var view = View(window);
            Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == window.ViewModel.Text.Get("CaptureTitle"));
            var sections = view.FindControl<StackPanel>("CaptureWorkflowContent")!;
            Assert.Equal(Regions.Skip(1), sections.Children.Select(item => item.Name));
            Assert.Equal(!fallback, sections.IsVisible);
            Assert.False(view.FindControl<Expander>("CapturePreviewDetails")!.IsExpanded);
            Assert.Equal(0, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(CaptureResultCode.Success, false, CaptureActiveState.NotActive)]
    [InlineData(CaptureResultCode.Success, true, CaptureActiveState.Active)]
    [InlineData(CaptureResultCode.AccessDenied, false, CaptureActiveState.CouldNotVerify)]
    [InlineData(CaptureResultCode.RegistryUnavailable, false, CaptureActiveState.CouldNotVerify)]
    public void ObservedStateAndReadFailure_RemainWorkflowValues(CaptureResultCode readCode, bool configured, CaptureActiveState expected)
    {
        var service = new Service { ReadCode = readCode, State = configured ? CrashCapturePolicy.Desired(new(false)) : new(false) };
        var flow = new CaptureWorkflow(service, new Journal()) { TargetExecutable = "synthetic.exe" };
        var window = Open(flow);
        try
        {
            Click(window, "CaptureReadButton");
            Assert.Equal(expected, flow.ActiveState);
            Assert.Contains(window.ViewModel.Text.Get("CaptureState" + expected), Find<TextBlock>(window, "CaptureCurrentState").Text);
            if (readCode != CaptureResultCode.Success)
                Assert.Contains(window.ViewModel.Text.Get("CaptureResult" + readCode), ((CapturePresentation)View(window).DataContext!).PreviewState, StringComparison.Ordinal);
            Assert.Equal(0, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(CaptureResultCode.Success, false, CaptureResultCode.Success)]
    [InlineData(CaptureResultCode.Success, true, CaptureResultCode.VerificationFailed)]
    [InlineData(CaptureResultCode.CancelledByUser, false, CaptureResultCode.CancelledByUser)]
    [InlineData(CaptureResultCode.HelperUnavailable, false, CaptureResultCode.HelperUnavailable)]
    [InlineData(CaptureResultCode.HelperIncompatible, false, CaptureResultCode.HelperIncompatible)]
    [InlineData(CaptureResultCode.AccessDenied, false, CaptureResultCode.AccessDenied)]
    [InlineData(CaptureResultCode.ApplyFailed, false, CaptureResultCode.ApplyFailed)]
    public void ExplicitModalConfigure_PreservesPendingAndFinalResult(CaptureResultCode response, bool skipWrite, CaptureResultCode expected)
    {
        var journal = new Journal();
        var service = new Service { Result = response, SkipWrite = skipWrite };
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        var window = Open(flow);
        try
        {
            service.BeforeExecute = () =>
            {
                Assert.Equal(CaptureResultCode.Pending, Assert.Single(journal.Items).Result);
                Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
                Assert.Null(flow.LastResult);
            };
            Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Click(window, "CaptureReadButton");
            Assert.Equal(0, service.Executions);
            Click(window, "CaptureConfigureButton");
            var dialog = Assert.IsType<CaptureConfirmationWindow>(Assert.Single(window.OwnedWindows));
            Assert.Equal(0, service.Executions);
            dialog.Close(false); Settle(window);
            Assert.Equal(0, service.Executions);
            Assert.Empty(journal.Items);
            Click(window, "CaptureConfigureButton");
            Confirm(window); Settle(window);
            Assert.Equal(1, service.Executions);
            Assert.Equal(expected, flow.LastResult!.Code);
            Assert.Equal(expected == CaptureResultCode.Success, ((CapturePresentation)View(window).DataContext!).HasVerifiedResult);
            Assert.Equal(expected, Assert.Single(flow.Entries).Result);
            Assert.Equal(expected == CaptureResultCode.Success, flow.Entries[0].RollbackAvailable);
            Assert.Contains(window.ViewModel.Text.Get("CaptureResult" + expected), Find<TextBlock>(window, "CaptureLastResult").Text);
            Assert.True(Find<Button>(window, "CaptureConfigureButton").IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RestoreActions_UseWorkflowDriftNewerActionAndRecoveryGuards()
    {
        var service = new Service(); var journal = new Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var original = Assert.Single(flow.Entries);
        var window = Open(flow);
        try
        {
            var executions = service.Executions;
            service.State = service.State with { DumpCount = 7 };
            Click(window, "CaptureRestoreButton" + original.ActionId.ToString("N")); Confirm(window); Settle(window);
            Assert.Equal(executions, service.Executions);
            Assert.Equal(CaptureResultCode.UnexpectedCurrentState, flow.LastResult!.Code);
            Assert.Contains(window.ViewModel.Text.Get("CaptureResultUnexpectedCurrentState"), Find<TextBlock>(window, "CaptureRestoreGuard").Text);
            service.State = original.RequestedState;
            var newer = original with { ActionId = Guid.NewGuid(), TimestampUtc = original.TimestampUtc.AddMinutes(1), Result = CaptureResultCode.Pending };
            await journal.SaveAsync(newer, CancellationToken.None); await flow.RefreshAsync(); Settle(window);
            Click(window, "CaptureRestoreButton" + original.ActionId.ToString("N")); Confirm(window); Settle(window);
            Assert.Equal(executions, service.Executions);
            Assert.Equal(CaptureResultCode.UnexpectedCurrentState, flow.LastResult!.Code);
            journal.Items.Remove(newer);
            Click(window, "CaptureRestoreButton" + original.ActionId.ToString("N")); Confirm(window); Settle(window);
            Assert.Equal(CaptureResultCode.Success, flow.LastResult!.Code);
            Assert.Equal(original.PreviousState, service.State);
            Assert.Equal("Complete", flow.Entries.Single(item => item.ActionId == original.ActionId).RollbackStatus);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "CaptureRestoreButton" + original.ActionId.ToString("N"));
            Assert.True(Find<Button>(window, "CaptureRefreshButton").IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PendingRecovery_NeverBecomesSuccessByOpeningOrRefreshing()
    {
        var before = new LocalDumpState(false); var journal = new Journal();
        var pending = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "recovery.exe", DateTimeOffset.UtcNow, true, before, CrashCapturePolicy.Desired(before));
        journal.Items.Add(pending);
        var service = new Service { State = pending.RequestedState };
        var flow = new CaptureWorkflow(service, journal); var window = Open(flow);
        try
        {
            Click(window, "CaptureRefreshButton");
            Assert.Equal(CaptureResultCode.Pending, flow.Entries[0].Result);
            Assert.Null(flow.LastResult);
            Click(window, "CaptureRestoreButton" + pending.ActionId.ToString("N")); Confirm(window); Settle(window);
            Assert.Equal(before, service.State);
            Assert.Equal("Complete", flow.Entries.Single(item => item.ActionId == pending.ActionId).RollbackStatus);
            var reopened = new CaptureWorkflow(service, journal); await reopened.RefreshAsync();
            Assert.Equal(CaptureResultCode.Pending, reopened.Entries.Single(item => item.ActionId == pending.ActionId).Result);
            Assert.False(CaptureWorkflow.CanRestore(reopened.Entries.Single(item => item.ActionId == pending.ActionId)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")][InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void RuntimeStateLocaleThemesWidthsAndKeyboard_PreserveViewAndMandatoryPreview(string language)
    {
        var service = new Service(); var flow = new CaptureWorkflow(service, new Journal()); var window = Open(flow);
        try
        {
            var view = View(window); var input = Find<TextBox>(window, "CaptureExecutable");
            input.Text = "first.exe";
            window.ViewModel.Navigate(AppPage.Home); window.ViewModel.Navigate(AppPage.System); Settle(window);
            Assert.Same(view, View(window)); Assert.Equal("first.exe", input.Text); Assert.Equal(0, service.Executions);
            input.Text = "bad/path.exe"; Click(window, "CaptureReadButton");
            Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            input.Text = "valid.exe"; Click(window, "CaptureReadButton");
            var preview = flow.Preview;
            var disclosure = view.FindControl<Expander>("CapturePreviewDetails")!;
            var toggle = disclosure.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(); toggle.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Settle(window);
            Assert.True(disclosure.IsExpanded);
            input.Focus();
            foreach (var theme in new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark })
            {
                window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = language, Theme = theme }); Settle(window);
                Assert.Same(view, View(window)); Assert.Same(preview, flow.Preview);
                Assert.True(disclosure.IsExpanded); Assert.True(input.IsFocused);
                Assert.Equal(window.ViewModel.Text.Get("CapturePreviewReady"), Find<TextBlock>(window, "CapturePreviewNotice").Text);
                Assert.Equal(window.ViewModel.Text.Get("CaptureExecutable"), AutomationProperties.GetName(input));
                foreach (var width in new[] { 560, 600, 640, 641, 1008, 1280, 1920 })
                {
                    window.Width = width; Settle(window);
                    AssertNoOverflow(window);
                    Assert.True(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
                }
            }
            window.ViewModel.Navigate(AppPage.Home); window.ViewModel.Navigate(AppPage.System); Settle(window);
            Assert.Same(view, View(window)); Assert.True(disclosure.IsExpanded);
            Assert.Equal("valid.exe", input.Text);
            input.Text = "changed.exe";
            Assert.Null(flow.Preview); Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Assert.Equal(window.ViewModel.Text.Get("CaptureNotRead"), Find<TextBlock>(window, "CapturePreviewNotice").Text);
            Assert.Equal(0, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task InFlightDispatch_DisablesCommandsAndAnnouncesOperationWithoutSuccess()
    {
        var release = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service { ExecuteGate = release.Task }; var journal = new Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        var window = Open(flow);
        try
        {
            Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Click(window, "CaptureReadButton"); Click(window, "CaptureConfigureButton"); Confirm(window);
            Assert.True(flow.IsBusy);
            Assert.Equal(CaptureResultCode.Pending, Assert.Single(journal.Items).Result);
            Assert.Null(flow.LastResult);
            Assert.False(((CapturePresentation)View(window).DataContext!).HasVerifiedResult);
            Assert.True(Find<TextBlock>(window, "CaptureOperationStatus").IsVisible);
            Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<TextBlock>(window, "CaptureOperationStatus")));
            foreach (var name in new[] { "CaptureReadButton", "CaptureRefreshButton", "CaptureConfigureButton" }) Assert.False(Find<Button>(window, name).IsEnabled);
            Click(window, "CaptureConfigureButton"); Assert.Empty(window.OwnedWindows); Assert.Equal(1, service.Executions);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            flow.Changed += () => { if (!flow.IsBusy) completed.TrySetResult(); };
            release.SetResult(new(CaptureResultCode.CancelledByUser));
            await completed.Task; Settle(window);
            Assert.Equal(CaptureResultCode.CancelledByUser, flow.LastResult!.Code);
            Assert.False(Find<TextBlock>(window, "CaptureOperationStatus").IsVisible);
        }
        finally { release.TrySetResult(new(CaptureResultCode.CancelledByUser)); window.Close(); }
    }

    [AvaloniaFact]
    public async Task InFlightRestore_DoesNotPresentPreviousSuccessAsThePendingOutcome()
    {
        var release = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new Service(); var journal = new Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var original = flow.Entries[0]; service.ExecuteGate = release.Task;
        var window = Open(flow);
        try
        {
            Click(window, "CaptureRestoreButton" + original.ActionId.ToString("N")); Confirm(window);
            Assert.True(flow.IsBusy);
            Assert.Equal(CaptureResultCode.Success, flow.LastResult!.Code); // Prior completed action remains domain history.
            Assert.False(((CapturePresentation)View(window).DataContext!).HasVerifiedResult);
            Assert.False(Find<TextBlock>(window, "CaptureLastResult").IsVisible);
            Assert.True(Find<TextBlock>(window, "CaptureOperationStatus").IsVisible);
            Assert.Contains(journal.Items, item => item.Operation == CaptureOperation.RestoreApplicationCrashDumpConfiguration && item.Result == CaptureResultCode.Pending);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            flow.Changed += () => { if (!flow.IsBusy) completed.TrySetResult(); };
            release.SetResult(new(CaptureResultCode.CancelledByUser)); await completed.Task; Settle(window);
            Assert.True(Find<TextBlock>(window, "CaptureLastResult").IsVisible);
            Assert.Equal(CaptureResultCode.CancelledByUser, flow.LastResult!.Code);
            Assert.NotEqual("Complete", flow.Entries.Single(item => item.ActionId == original.ActionId).RollbackStatus);
        }
        finally { release.TrySetResult(new(CaptureResultCode.CancelledByUser)); window.Close(); }
    }

    internal sealed class Journal : ICaptureJournal
    {
        public List<CaptureJournalEntry> Items { get; } = [];
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token) { Items.RemoveAll(item => item.ActionId == entry.ActionId); Items.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>(Items.ToArray());
    }
    internal sealed class Service : ICrashCaptureService
    {
        public LocalDumpState State { get; set; } = new(false);
        public CaptureResultCode ReadCode { get; set; } = CaptureResultCode.Success;
        public CaptureResultCode Result { get; set; } = CaptureResultCode.Success;
        public bool SkipWrite { get; set; }
        public int Executions { get; private set; }
        public Action? BeforeExecute { get; set; }
        public Task<CaptureResult>? ExecuteGate { get; set; }
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) => Task.FromResult(new CaptureReadResult(CrashCapturePolicy.IsValidExecutable(executable) ? ReadCode : CaptureResultCode.InvalidRequest, ReadCode == CaptureResultCode.Success && CrashCapturePolicy.IsValidExecutable(executable) ? State : null));
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token) { BeforeExecute?.Invoke(); Executions++; if (ExecuteGate is not null) return ExecuteGate; if (Result == CaptureResultCode.Success && !SkipWrite) State = request.DesiredState; return Task.FromResult(new CaptureResult(Result, State)); }
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The window owns its view model and every caller closes it in finally.")]
    internal static MainWindow Open(CaptureWorkflow? capture, string language = "en")
    {
        var window = new MainWindow(new MainViewModel(new TestServices { Capture = capture, Settings = new UserSettings(Language: language) }));
        window.Width = 1280; window.Height = 1000; window.Show(); window.ViewModel.Navigate(AppPage.System); Settle(window); return window;
    }
    internal static CaptureView View(MainWindow window) => window.GetVisualDescendants().OfType<CaptureView>().Single();
    internal static void Settle(MainWindow window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    internal static T Find<T>(MainWindow window, string name) where T : Control { Settle(window); return window.GetVisualDescendants().OfType<T>().Single(item => item.Name == name); }
    internal static void Click(MainWindow window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    internal static void Confirm(MainWindow window) { Assert.IsType<CaptureConfirmationWindow>(Assert.Single(window.OwnedWindows)).Close(true); Settle(window); }
    internal static void AssertNoOverflow(MainWindow window) => Assert.All(window.GetVisualDescendants().OfType<ScrollViewer>(), viewer => Assert.True(viewer.Viewport.Width == 0 || viewer.Extent.Width <= viewer.Viewport.Width + 2));
}
