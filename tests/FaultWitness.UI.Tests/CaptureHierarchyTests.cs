using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class CaptureHierarchyTests
{
    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public async Task PolicyConsentAndReadOnlyPreview_PreserveAllSafetyTextAndGuards(string language)
    {
        var service = new CaptureAxamlTests.Service();
        var flow = new CaptureWorkflow(service, new CaptureAxamlTests.Journal()) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync();
        var window = CaptureAxamlTests.Open(flow, language);
        try
        {
            var view = CaptureAxamlTests.View(window);
            var projection = (CapturePresentation)view.DataContext!;
            var text = window.ViewModel.Text;
            Assert.Equal(flow.TargetExecutable, projection.PolicyTarget);
            Assert.Equal(text.Format("CapturePolicyLimitValue", CrashCapturePolicy.DumpCount), projection.PolicyLimit);
            Assert.False(projection.HasVerifiedResult);
            Assert.DoesNotContain("verified", view.FindControl<Border>("CaptureResultRegion")!.Classes);
            Assert.Equal(text.Get("CapturePreviewReady"), view.FindControl<TextBlock>("CapturePreviewNotice")!.Text);
            AssertOwner(window, "CapturePreviewNotice");
            var safety = view.FindControl<StackPanel>("CaptureSafetyRegion")!;
            Assert.True(safety.IsVisible);
            Assert.Empty(safety.GetVisualAncestors().OfType<Expander>());
            foreach (var key in new[] { "CaptureRequiresAdministrator", "CapturePreviewGuide", "CapturePrivacy", "CaptureFolder", "CaptureLimitations" })
                Assert.Contains(safety.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == text.Get(key));
            Assert.Equal(800, view.FindControl<StackPanel>("CaptureContent")!.MaxWidth);
            Assert.False(CaptureAxamlTests.Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            CaptureAxamlTests.Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Assert.True(CaptureAxamlTests.Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Assert.NotEqual(AutomationProperties.GetName(CaptureAxamlTests.Find<Button>(window, "CaptureReadButton")),
                AutomationProperties.GetName(CaptureAxamlTests.Find<Button>(window, "CaptureConfigureButton")));
            var input = CaptureAxamlTests.Find<TextBox>(window, "CaptureExecutable");
            input.Focus(); input.Text = "changed.exe";
            CaptureAxamlTests.Settle(window);
            Assert.True(input.IsFocused);
            Assert.Null(flow.Preview);
            Assert.False(projection.CanConfigure);
            Assert.Equal(text.Get("CaptureNotRead"), projection.PreviewNotice);
            Assert.Equal("changed.exe", projection.PolicyTarget);
            Assert.Equal(0, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(CaptureResultCode.Success, false, "verified")]
    [InlineData(CaptureResultCode.Success, true, "error")]
    [InlineData(CaptureResultCode.CancelledByUser, false, "attention")]
    [InlineData(CaptureResultCode.ApplyFailed, false, "error")]
    [InlineData(CaptureResultCode.HelperUnavailable, false, "error")]
    [InlineData(CaptureResultCode.HelperIncompatible, false, "error")]
    [InlineData(CaptureResultCode.AccessDenied, false, "error")]
    [InlineData(CaptureResultCode.VerificationFailed, false, "error")]
    public async Task ResultSurface_ProjectsRecordedOutcomeWithoutExtraExecutions(CaptureResultCode response, bool skipWrite, string role)
    {
        var service = new CaptureAxamlTests.Service { Result = response, SkipWrite = skipWrite };
        var journal = new CaptureAxamlTests.Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var result = flow.LastResult;
        var entry = Assert.Single(journal.Items);
        var window = CaptureAxamlTests.Open(flow);
        try
        {
            var view = CaptureAxamlTests.View(window);
            var region = view.FindControl<Border>("CaptureResultRegion")!;
            Assert.Contains(role, region.Classes);
            Assert.Equal(role == "verified", view.FindControl<TextBlock>("CaptureVerifiedResult")!.IsVisible);
            Assert.Equal(role == "verified", ((CapturePresentation)view.DataContext!).HasVerifiedResult);
            var displayed = view.FindControl<TextBlock>("CaptureLastResult")!;
            Assert.Equal(window.ViewModel.Text.Format("CaptureLastResultValue", window.ViewModel.Text.Get("CaptureResult" + result!.Code)), displayed.Text);
            Assert.Equal(displayed.Text, AutomationProperties.GetName(displayed));
            AssertOwner(window, "CaptureLastResult");
            window.ViewModel.ChangeSettings(window.ViewModel.Settings with { Language = "de", Theme = AppTheme.Dark });
            CaptureAxamlTests.Settle(window);
            AssertOwner(window, "CaptureLastResult");
            Assert.Same(result, flow.LastResult);
            Assert.Equal(entry, Assert.Single(journal.Items));
            Assert.Equal(1, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BusyNavigation_UsesOneOwnerAndNeverShowsPriorVerifiedSuccess()
    {
        var service = new CaptureAxamlTests.Service();
        var journal = new CaptureAxamlTests.Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var gate = new TaskCompletionSource<CaptureResult>(); service.ExecuteGate = gate.Task;
        var window = CaptureAxamlTests.Open(flow);
        var view = CaptureAxamlTests.View(window);
        var running = flow.RestoreAsync(flow.Entries[0].ActionId);
        try
        {
            CaptureAxamlTests.Settle(window);
            Assert.True(flow.IsBusy);
            Assert.Contains(journal.Items, item => item.Operation == CaptureOperation.RestoreApplicationCrashDumpConfiguration && item.Result == CaptureResultCode.Pending);
            Assert.DoesNotContain("verified", view.FindControl<Border>("CaptureResultRegion")!.Classes);
            Assert.False(view.FindControl<TextBlock>("CaptureLastResult")!.IsVisible);
            Assert.False(view.FindControl<StackPanel>("CapturePreviewRegion")!.IsVisible);
            AssertOwner(window, "CaptureOperationStatus");
            window.ViewModel.Navigate(AppPage.Home); window.ViewModel.Navigate(AppPage.Capture);
            CaptureAxamlTests.Settle(window);
            Assert.Same(view, CaptureAxamlTests.View(window));
            AssertOwner(window, "CaptureOperationStatus");
            gate.SetResult(new(CaptureResultCode.CancelledByUser)); await running;
            CaptureAxamlTests.Settle(window);
            AssertOwner(window, "CaptureLastResult");
            Assert.Equal(CaptureResultCode.CancelledByUser, flow.LastResult!.Code);
            Assert.Equal(2, service.Executions);
        }
        finally { gate.TrySetResult(new(CaptureResultCode.CancelledByUser)); await running; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task PreviousStateSummary_KeepsExactRecordAndRawValuesInClosedDetail(bool present)
    {
        var before = present ? new LocalDumpState(true, 2, 5, @"%LOCALAPPDATA%\CrashDumps") : new(false);
        var service = new CaptureAxamlTests.Service { State = before };
        var journal = new CaptureAxamlTests.Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var recorded = Assert.Single(journal.Items);
        var window = CaptureAxamlTests.Open(flow);
        try
        {
            var row = Assert.Single(((CapturePresentation)CaptureAxamlTests.View(window).DataContext!).Entries);
            Assert.True(row.CanRestore);
            Assert.Equal(CaptureWorkflow.CanRestore(recorded), row.CanRestore);
            Assert.Equal(present ? row.Previous : window.ViewModel.Text.Get("CapturePreviousAbsent"), row.PreviousSummary);
            if (!present) Assert.DoesNotContain(window.ViewModel.Text.Get("NotAvailable"), row.PreviousSummary, StringComparison.Ordinal);
            var card = CaptureAxamlTests.View(window).GetVisualDescendants().OfType<App.Views.Components.CaptureRestoreCard>().Single();
            Assert.Equal(row.PreviousSummary, card.FindControl<TextBlock>("CapturePreviousSummary")!.Text);
            var detail = card.GetVisualDescendants().OfType<Expander>().Single();
            Assert.False(detail.IsExpanded);
            detail.IsExpanded = true; CaptureAxamlTests.Settle(window);
            Assert.Contains(detail.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == row.Previous);
            Assert.Equal(recorded, Assert.Single(journal.Items));
            Assert.Equal(before, recorded.PreviousState);
            Assert.Equal(1, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ExistingRefusal_IsAdjacentToRecoveryAndHasNoSecondAnnouncement(bool newer)
    {
        var service = new CaptureAxamlTests.Service(); var journal = new CaptureAxamlTests.Journal();
        var flow = new CaptureWorkflow(service, journal) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var original = flow.Entries[0];
        if (newer) journal.Items.Add(original with { ActionId = Guid.NewGuid(), TimestampUtc = original.TimestampUtc.AddMinutes(1), Result = CaptureResultCode.Pending });
        else service.State = service.State with { DumpCount = 7 };
        await flow.RestoreAsync(original.ActionId);
        var window = CaptureAxamlTests.Open(flow);
        try
        {
            var view = CaptureAxamlTests.View(window);
            var guard = view.FindControl<TextBlock>("CaptureRestoreGuard")!;
            Assert.True(guard.IsVisible);
            Assert.Contains(guard, view.FindControl<StackPanel>("CaptureRestoreRegion")!.Children);
            Assert.Equal(window.ViewModel.Text.Get("CaptureResultUnexpectedCurrentState"), guard.Text);
            Assert.Equal(guard.Text, AutomationProperties.GetName(guard));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(guard));
            AssertOwner(window, "CaptureLastResult");
            Assert.Equal(CaptureResultCode.UnexpectedCurrentState, flow.LastResult!.Code);
            Assert.Equal(newer ? 2 : 1, journal.Items.Count);
            Assert.Equal(1, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)][InlineData(true)]
    public void HistoricalPending_IsUnverifiedAndDoesNotBecomeLatestSuccess(bool restored)
    {
        var before = new LocalDumpState(false);
        var pending = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "recovery.exe", DateTimeOffset.UtcNow, true, before, CrashCapturePolicy.Desired(before), RollbackStatus: restored ? "Complete" : "NotRequested");
        var journal = new CaptureAxamlTests.Journal(); journal.Items.Add(pending);
        var service = new CaptureAxamlTests.Service { State = pending.RequestedState };
        var flow = new CaptureWorkflow(service, journal);
        var window = CaptureAxamlTests.Open(flow);
        try
        {
            var projection = (CapturePresentation)CaptureAxamlTests.View(window).DataContext!;
            var row = Assert.Single(projection.Entries);
            Assert.True(row.IsPending);
            Assert.Equal(window.ViewModel.Text.Get("CaptureResultPending"), row.Status);
            Assert.Equal(!restored, row.CanRestore);
            Assert.False(projection.HasVerifiedResult);
            Assert.Null(flow.LastResult);
            Assert.Equal(CaptureResultCode.Pending, Assert.Single(journal.Items).Result);
            Assert.Contains("pending", CaptureAxamlTests.Find<TextBlock>(window, "CaptureJournalStatus").Classes);
            Assert.Equal(0, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardOrder_ReachesGuardConfigureRecoveryThenDisclosure()
    {
        var service = new CaptureAxamlTests.Service();
        var flow = new CaptureWorkflow(service, new CaptureAxamlTests.Journal()) { TargetExecutable = "synthetic.exe" };
        await flow.PreviewAsync(); await flow.ConfigureAsync();
        var window = CaptureAxamlTests.Open(flow);
        try
        {
            CaptureAxamlTests.Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            CaptureAxamlTests.Find<TextBox>(window, "CaptureExecutable").Focus();
            foreach (var name in new[] { "CaptureReadButton", "CaptureRefreshButton", "CaptureArchitectureConfirmation", "CaptureConfigureButton", "CaptureRestoreButton" + flow.Entries[0].ActionId.ToString("N") })
            {
                Press(window, Key.Tab, PhysicalKey.Tab, "\t");
                Assert.True(CaptureAxamlTests.Find<Control>(window, name).IsFocused, name);
            }
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            var card = CaptureAxamlTests.View(window).GetVisualDescendants().OfType<App.Views.Components.CaptureRestoreCard>().Single();
            var detail = card.GetVisualDescendants().OfType<Expander>().Single();
            Assert.True(detail.GetVisualDescendants().OfType<ToggleButton>().First().IsFocused);
            Press(window, Key.Space, PhysicalKey.Space, null);
            Assert.True(detail.IsExpanded);
            Assert.Equal(1, service.Executions);
        }
        finally { window.Close(); }
    }

    internal static void AssertOwner(MainWindow window, string name)
    {
        CaptureAxamlTests.Settle(window);
        var owners = CaptureAxamlTests.View(window).GetVisualDescendants().OfType<Control>()
            .Where(item => item.IsEffectivelyVisible && AutomationProperties.GetLiveSetting(item) != AutomationLiveSetting.Off).ToArray();
        Assert.Equal(name, Assert.Single(owners).Name);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(owners[0]));
    }

    private static void Press(MainWindow window, Key key, PhysicalKey physical, string? text)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, text);
        window.KeyRelease(key, RawInputModifiers.None, physical, text);
        CaptureAxamlTests.Settle(window);
    }
}
