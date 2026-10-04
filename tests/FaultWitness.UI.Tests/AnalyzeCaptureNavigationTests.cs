using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

#pragma warning disable CA2000 // Each window owns its view model and is closed in finally.

namespace FaultWitness.UI.Tests;

public sealed class AnalyzeCaptureNavigationTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMode.Recent)]
    [InlineData(AnalysisMode.Around)]
    [InlineData(AnalysisMode.Files)]
    public async Task SwitchingSubpagesPreservesModeInputsImportsAndCapturePreview(AnalysisMode mode)
    {
        var service = new TrackingCaptureService();
        var flow = new CaptureWorkflow(service, new CaptureAxamlTests.Journal());
        var window = Open(new TestServices { Capture = flow });
        try
        {
            var vm = window.ViewModel;
            vm.OpenAnalyze(mode);
            var analyze = Current<AnalyzeView>(window);
            var state = Assert.IsType<AnalyzePresentation>(analyze.DataContext);
            state.SelectPeriod((int)AnalysisPeriod.Custom);
            state.CustomFromDate = DateTimeOffset.Now.AddDays(-4);
            state.CustomToDate = DateTimeOffset.Now.AddDays(-1);
            state.AroundDate = DateTimeOffset.Now.AddDays(-2);
            state.AroundClock = new TimeSpan(13, 47, 0);
            state.WindowIndex = 3;
            vm.AddImports([@"C:\synthetic\record.wer"]);
            var inputs = (state.CustomFromDate, state.CustomToDate, state.AroundDate, state.AroundClock, state.WindowIndex);
            var imports = vm.Imports.ToArray();
            Click(window, "AnalyzeCaptureTab");
            var capture = CaptureAxamlTests.View(window);
            capture.FindControl<TextBox>("CaptureExecutable")!.Text = "chosen.exe";
            await flow.PreviewAsync();
            var preview = flow.Preview;
            capture.FindControl<CheckBox>("CaptureArchitectureConfirmation")!.IsChecked = true;
            capture.FindControl<Expander>("CapturePreviewDetails")!.IsExpanded = true;
            var reads = service.Reads;
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.System })
            {
                Click(window, "AnalyzeWorkflowTab");
                vm.ChangeSettings(vm.Settings with { Language = "de", Theme = theme });
                window.Width = theme == AppTheme.Light ? 600 : 1280;
                CaptureAxamlTests.Settle(window);
                Assert.Same(analyze, Current<AnalyzeView>(window));
                Assert.Equal(mode, vm.AnalysisMode);
                Assert.Equal(inputs, (state.CustomFromDate, state.CustomToDate, state.AroundDate, state.AroundClock, state.WindowIndex));
                Assert.Equal(AnalysisPeriod.Custom, state.SelectedPeriod);
                Assert.Equal(imports, vm.Imports);
                Click(window, "AnalyzeCaptureTab");
                Assert.Same(capture, CaptureAxamlTests.View(window));
                Assert.Same(preview, flow.Preview);
                Assert.Equal("chosen.exe", flow.TargetExecutable);
                Assert.True(capture.FindControl<CheckBox>("CaptureArchitectureConfirmation")!.IsChecked);
                Assert.True(capture.FindControl<Expander>("CapturePreviewDetails")!.IsExpanded);
                Assert.Equal(reads, service.Reads);
                Assert.Equal(0, service.Inner.Executions);
            }
            capture.FindControl<TextBox>("CaptureExecutable")!.Text = "changed.exe";
            vm.Navigate(AppPage.Home);
            Click(window, "NavAnalyze");
            Assert.Equal(AppPage.Capture, vm.Page);
            Assert.Null(flow.Preview);
            Assert.Null(flow.LastResult);
            Assert.Equal(reads, service.Reads);
            vm.OpenAnalyze(mode);
            Assert.Same(analyze, Current<AnalyzeView>(window));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void DefaultRouteSingleCaptureAndLocalizedSelectionRemainStable(string locale)
    {
        var service = new TrackingCaptureService();
        var window = Open(new TestServices { Capture = new CaptureWorkflow(service, new CaptureAxamlTests.Journal()) });
        try
        {
            Click(window, "NavAnalyze");
            Assert.Equal(AppPage.Analyze, window.ViewModel.Page);
            Assert.IsType<AnalyzeView>(window.FindControl<ContentControl>("PageHost")!.Content);
            foreach (var theme in new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark })
            {
                window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = locale, Theme = theme });
                AssertSelection(window, "AnalyzeWorkflow", "Analyze");
                Click(window, "AnalyzeCaptureTab");
                AssertSelection(window, "AnalyzeCapture", "CaptureTitle");
                Assert.Contains("selected", Find<Button>(window, "NavAnalyze").Classes);
                Assert.Single(window.GetVisualDescendants().OfType<CaptureView>());
                var title = Assert.Single(Current<AnalyzeCaptureView>(window).GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("page-title"));
                Assert.Equal(window.ViewModel.Text.Get("Analyze"), title.Text);
                window.ViewModel.Navigate(AppPage.System);
                Assert.Empty(window.GetVisualDescendants().OfType<CaptureView>());
                Assert.Null(Current<SystemView>(window).FindControl<ContentControl>("CaptureHost"));
                Assert.NotNull(Find<Button>(window, "SystemInformationTab"));
                Assert.NotNull(Find<Button>(window, "SystemReadinessTab"));
                window.ViewModel.Navigate(AppPage.Readiness);
                Assert.Empty(window.GetVisualDescendants().OfType<CaptureView>());
                Assert.Contains("selected", Find<Button>(window, "NavSystem").Classes);
                Click(window, "NavAnalyze");
                Assert.Equal(AppPage.Capture, window.ViewModel.Page);
                Click(window, "AnalyzeWorkflowTab");
            }
            Assert.Equal(0, service.Inner.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void KeyboardOrderKeepsFocusOnSelectedSubpageThenItsContent(AppTheme theme)
    {
        var window = Open(new TestServices { Settings = new UserSettings(Language: "en", Theme: theme) });
        try
        {
            window.ViewModel.Navigate(AppPage.Analyze);
            var analyze = Current<AnalyzeView>(window);
            Assert.True(Find<Button>(window, "AnalyzeWorkflowTab").Focus());
            Key(window, Avalonia.Input.Key.Tab, PhysicalKey.Tab);
            Assert.True(Find<Button>(window, "AnalyzeCaptureTab").IsFocused);
            Key(window, Avalonia.Input.Key.Space, PhysicalKey.Space);
            Assert.Equal(AppPage.Capture, window.ViewModel.Page);
            Assert.True(Find<Button>(window, "AnalyzeCaptureTab").IsFocused);
            Key(window, Avalonia.Input.Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.True(Find<Button>(window, "AnalyzeWorkflowTab").IsFocused);
            Key(window, Avalonia.Input.Key.Space, PhysicalKey.Space);
            Assert.Same(analyze, Current<AnalyzeView>(window));
            Assert.True(Find<Button>(window, "AnalyzeWorkflowTab").IsFocused);
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = "pl", Theme = AppTheme.System });
            Assert.True(Find<Button>(window, "AnalyzeWorkflowTab").IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnalysisBusyCancelOwnershipSurvivesCaptureNavigationWithoutExtraCalls()
    {
        var services = new TestServices { WaitForCancellation = true };
        var window = Open(services);
        Task? running = null;
        try
        {
            window.ViewModel.Navigate(AppPage.Analyze);
            running = window.ViewModel.AnalyzeAsync();
            CaptureAxamlTests.Settle(window);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<TextBlock>(window, "AnalyzeFeedback")));
            Click(window, "AnalyzeCaptureTab");
            Assert.True(window.ViewModel.IsBusy);
            Assert.True(Find<TextBlock>(window, "StatusText").IsVisible);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<TextBlock>(window, "StatusText")));
            Click(window, "AnalyzeWorkflowTab");
            Assert.False(Find<TextBlock>(window, "StatusText").IsVisible);
            Click(window, "CancelAnalyzeLocal");
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("AnalysisCancelled", window.ViewModel.StatusKey);
            Assert.Equal(1, services.AnalysisCalls);
            Assert.Equal(0, services.Saved);
        }
        finally { window.ViewModel.Cancel(); if (running is not null) await running; window.Close(); }
    }

    [AvaloniaFact]
    public async Task CaptureBusyUsesOneVisibleOwnerAndDoesNotResetPendingOrExecuteAgain()
    {
        var gate = new TaskCompletionSource<CaptureResult>();
        var service = new TrackingCaptureService();
        var journal = new CaptureAxamlTests.Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        var window = Open(new TestServices { Capture = flow });
        Task? running = null;
        try
        {
            window.ViewModel.Navigate(AppPage.Capture);
            CaptureAxamlTests.Settle(window);
            var view = CaptureAxamlTests.View(window);
            service.Inner.ExecuteGate = gate.Task;
            running = flow.ConfigureAsync();
            Assert.True(flow.IsBusy);
            Assert.Equal(CaptureResultCode.Pending, Assert.Single(journal.Items).Result);
            Assert.False(Find<TextBlock>(window, "StatusText").IsVisible);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Find<TextBlock>(window, "CaptureOperationStatus")));
            Click(window, "AnalyzeWorkflowTab");
            Assert.True(flow.IsBusy);
            Assert.Empty(window.GetVisualDescendants().OfType<CaptureView>());
            Assert.Equal(window.ViewModel.Text.Get("CaptureOperationRunning"), Find<TextBlock>(window, "StatusText").Text);
            Assert.True(Find<TextBlock>(window, "StatusText").IsVisible);
            Assert.False(Find<Button>(window, "CancelAnalysis").IsVisible);
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = "ru", Theme = AppTheme.Dark });
            Assert.Equal(window.ViewModel.Text.Get("CaptureOperationRunning"), Find<TextBlock>(window, "StatusText").Text);
            Click(window, "AnalyzeCaptureTab");
            Assert.Same(view, CaptureAxamlTests.View(window));
            Assert.False(Find<TextBlock>(window, "StatusText").IsVisible);
            gate.SetResult(new(CaptureResultCode.CancelledByUser));
            await running;
            CaptureAxamlTests.Settle(window);
            Assert.Equal(CaptureResultCode.CancelledByUser, flow.LastResult!.Code);
            Assert.Equal(1, service.Inner.Executions);
            Assert.Equal(CaptureResultCode.CancelledByUser, Assert.Single(journal.Items).Result);
        }
        finally { gate.TrySetResult(new(CaptureResultCode.CancelledByUser)); if (running is not null) await running; window.Close(); }
    }

    private static void AssertSelection(MainWindow window, string name, string key)
    {
        var tab = Find<Button>(window, name + "Tab");
        Assert.Contains("selected", tab.Classes);
        Assert.Equal(window.ViewModel.Text.Get(key), AutomationProperties.GetName(tab));
        Assert.Equal(window.ViewModel.Text.Get("NavigationCurrent"), AutomationProperties.GetHelpText(tab));
        Assert.Equal(1, Find<Border>(window, name + "Indicator").Opacity);
        Assert.True(tab.Focusable);
        Assert.True(tab.IsTabStop);
    }

    private static void Key(MainWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        CaptureAxamlTests.Settle(window);
    }

    internal static MainWindow Open(TestServices services)
    {
        var window = new MainWindow(new MainViewModel(services));
        VisualRenderGeometry.ShowAtRequestedGeometry(window, 1280, 1000);
        return window;
    }

    internal static T Current<T>(MainWindow window) where T : Control => Assert.IsType<T>(window.FindControl<ContentControl>("PageHost")!.Content);
    internal static T Find<T>(MainWindow window, string name) where T : Control => CaptureAxamlTests.Find<T>(window, name);
    internal static void Click(MainWindow window, string name)
    {
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CaptureAxamlTests.Settle(window);
    }

    private sealed class TrackingCaptureService : ICrashCaptureService
    {
        public CaptureAxamlTests.Service Inner { get; } = new();
        public int Reads { get; private set; }
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) { Reads++; return Inner.ReadAsync(executable, token); }
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token) => Inner.ExecuteAsync(request, token);
    }
}
