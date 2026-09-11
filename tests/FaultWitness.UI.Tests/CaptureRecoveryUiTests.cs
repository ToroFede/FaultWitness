using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

public sealed class CaptureRecoveryUiTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureRequiresPreviewAndConfirmationInEitherOrder(bool confirmationFirst)
    {
        var capture = new CaptureWorkflow(new Service(), new Journal());
        var window = Open(capture);
        try
        {
            Find<TextBox>(window, "CaptureExecutable").Text = "demo.exe";
            Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            if (confirmationFirst) Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Click(window, "CaptureReadButton");
            Assert.Equal(confirmationFirst, Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Assert.True(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            window.ViewModel.Notify("Ready"); // Shared busy/status updates must preserve capture guards.
            Assert.True(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Find<TextBox>(window, "CaptureExecutable").Text = "other.exe";
            Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Assert.Null(capture.Preview);
            var preview = window.GetLogicalDescendants().OfType<TextBlock>().Single(x => x.Name == "CapturePreviewState");
            Assert.Contains(window.ViewModel.Text.Get("CaptureNotRead"), preview.Text);
            window.ViewModel.Notify("Ready");
            Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UnsupportedPreviewRemainsReadOnlyAfterConfirmationAndStatusRefresh()
    {
        var service = new Service { State = new(true, 1, 3, @"C:\external") };
        var capture = new CaptureWorkflow(service, new Journal()); var window = Open(capture);
        try
        {
            Find<TextBox>(window, "CaptureExecutable").Text = "demo.exe";
            Find<CheckBox>(window, "CaptureArchitectureConfirmation").IsChecked = true;
            Click(window, "CaptureReadButton"); window.ViewModel.Notify("Ready");
            Assert.False(Find<Button>(window, "CaptureConfigureButton").IsEnabled);
            Assert.Equal(0, service.Executions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MoreThanOneThousandRetainedEntriesRemainReachableViaShowMore()
    {
        var before = new LocalDumpState(false);
        var template = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "history0.exe", DateTimeOffset.UtcNow.AddDays(-10), true, before, CrashCapturePolicy.Desired(before), CaptureResultCode.Success);
        var journal = new Journal();
        for (var i = 0; i < 1003; i++)
            journal.Items.Add(template with { ActionId = Guid.NewGuid(), TargetExecutable = $"history{i}.exe" });
        var capture = new CaptureWorkflow(new Service(), journal);
        var window = Open(capture);
        try
        {
            var pages = 0;
            while (window.GetVisualDescendants().OfType<Button>().Any(x => x.Name == "CaptureShowMoreButton"))
            {
                Click(window, "CaptureShowMoreButton");
                pages++;
            }
            Assert.Equal(50, pages);
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), row =>
                AutomationProperties.GetName(row)?.Contains("history1002.exe", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void ReopenedJournalExposesEveryOlderRecoveryWithoutRelabelingPending(string language)
    {
        var before = new LocalDumpState(false);
        var pending = new CaptureJournalEntry(Guid.NewGuid(), CaptureOperation.ConfigureApplicationCrashDump, "pending.exe", DateTimeOffset.UtcNow.AddDays(-2), true, before, CrashCapturePolicy.Desired(before));
        var ordinary = pending with { ActionId = Guid.NewGuid(), TargetExecutable = "ordinary.exe", Result = CaptureResultCode.Success, RollbackAvailable = true };
        var journal = new Journal();
        for (var i = 0; i < 25; i++) journal.Items.Add(ordinary with { ActionId = Guid.NewGuid(), TargetExecutable = $"new{i}.exe" });
        journal.Items.Add(pending); journal.Items.Add(ordinary);
        journal.Items.Add(pending with { ActionId = Guid.NewGuid(), RollbackStatus = "Complete" });
        var capture = new CaptureWorkflow(new Service(), journal); var window = Open(capture, language);
        try
        {
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(x => x.Name?.StartsWith("CaptureRestoreButton", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(20, buttons.Length);
            Assert.DoesNotContain(buttons, x => x.Name == "CaptureRestoreButton" + pending.ActionId.ToString("N"));
            Assert.NotEqual("CaptureShowMore", Find<Button>(window, "CaptureShowMoreButton").Content?.ToString());
            Click(window, "CaptureShowMoreButton");
            Assert.Equal(27, window.GetVisualDescendants().OfType<Button>().Count(x => x.Name?.StartsWith("CaptureRestoreButton", StringComparison.Ordinal) == true));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), x => x.Name == "CaptureShowMoreButton");
            var recovery = Find<Button>(window, "CaptureRestoreButton" + pending.ActionId.ToString("N"));
            Assert.True(recovery.IsEnabled);
            var row = recovery.GetVisualAncestors().OfType<Border>().First();
            Assert.Contains(window.ViewModel.Text.Get("CaptureResultPending"), AutomationProperties.GetName(row));
            Assert.Equal(CaptureResultCode.Pending, capture.Entries.Single(x => x.ActionId == pending.ActionId).Result);
            Click(window, recovery.Name!);
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal(window.ViewModel.Text.Get("CaptureRestore"), dialog.Title);
            dialog.Close(false);
        }
        finally { window.Close(); }
    }

    private static MainWindow Open(CaptureWorkflow capture, string language = "en")
    {
#pragma warning disable CA2000 // MainWindow disposes the view model on Closed in each test finally.
        var window = new MainWindow(new MainViewModel(new TestServices { Capture = capture, Settings = new UserSettings(Language: language) }));
#pragma warning restore CA2000
        window.Show(); window.ViewModel.Navigate(AppPage.System); window.UpdateLayout(); return window;
    }
    private static T Find<T>(MainWindow window, string name) where T : Control { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window.GetVisualDescendants().OfType<T>().Single(x => x.Name == name); }
    private static void Click(MainWindow window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); }
    private sealed class Journal : ICaptureJournal
    {
        public List<CaptureJournalEntry> Items { get; } = [];
        public Task SaveAsync(CaptureJournalEntry entry, CancellationToken token) { Items.RemoveAll(x => x.ActionId == entry.ActionId); Items.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<CaptureJournalEntry>> LoadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureJournalEntry>>(Items.ToArray());
    }
    private sealed class Service : ICrashCaptureService
    {
        public LocalDumpState State { get; set; } = new(false);
        public int Executions { get; private set; }
        public Task<CaptureReadResult> ReadAsync(string executable, CancellationToken token) => Task.FromResult(new CaptureReadResult(CaptureResultCode.Success, State));
        public Task<CaptureResult> ExecuteAsync(CaptureRequest request, CancellationToken token) { Executions++; State = request.DesiredState; return Task.FromResult(new CaptureResult(CaptureResultCode.Success, State)); }
    }
}
