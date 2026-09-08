using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

public sealed class HistorySelectionRegressionTests
{
    [Fact]
    public void SelectingCurrentHistoryRow_DoesNotRaiseAnotherPageChange()
    {
        using var viewModel = new MainViewModel(new TestServices());
        var row = new HistoryRow(Stored("first", "recent", "coverage-first"), viewModel.Text);
        var pageChanges = 0;
        var selectionChanges = 0;
        viewModel.Changed += change =>
        {
            if (change == ViewChange.Page) pageChanges++;
            if (change == ViewChange.HistorySelection) selectionChanges++;
        };

        viewModel.SelectHistory(row);
        viewModel.SelectHistory(row);
        viewModel.SelectHistory(row);

        Assert.Same(row, viewModel.SelectedHistory);
        Assert.Equal(0, pageChanges);
        Assert.Equal(1, selectionChanges);
    }

    [AvaloniaFact]
    public async Task HistorySelection_AcrossRepeatedViewRebuilds_RemainsBoundedAndUpdatesDetail()
    {
        var first = Stored("first", "recent", "coverage-first");
        var second = Stored("second", "around", "coverage-second");
        var window = Open(new TestServices { History = [first, second] });

        try
        {
            await window.ViewModel.RefreshHistoryAsync();
            window.ViewModel.Navigate(AppPage.History);

            for (var iteration = 0; iteration < 12; iteration++)
            {
                window.ViewModel.Navigate(AppPage.Home);
                await window.ViewModel.RefreshHistoryAsync();
                window.ViewModel.Navigate(AppPage.History);
                window.UpdateLayout();

                var expected = iteration % 2 == 0 ? second : first;
                var list = Find<ListBox>(window, "HistoryList");
                list.SelectedItem = window.ViewModel.History.Single(row => row.Scan.Id == expected.Id);
                window.UpdateLayout();

                Assert.Equal(expected.Id, window.ViewModel.SelectedHistory?.Scan.Id);
                Assert.Same(list, Find<ListBox>(window, "HistoryList"));
                var coverage = window.GetVisualDescendants().OfType<Expander>()
                    .Single(item => Equals(item.Header, window.ViewModel.Text.Get("SourceCoverage")));
                coverage.IsExpanded = true;
                window.UpdateLayout();
                var visibleText = VisibleText(window);
                Assert.Contains(expected.Metadata!.CoverageSummary!, visibleText, StringComparison.Ordinal);
                Assert.DoesNotContain(expected.Id == first.Id ? second.Metadata!.CoverageSummary! : first.Metadata!.CoverageSummary!, visibleText, StringComparison.Ordinal);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static StoredScan Stored(string id, string analysisType, string coverage)
    {
        var started = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var metadata = new ScanHistoryMetadata(analysisType, started, started.AddMinutes(1), 10, 0, 0, 0, coverage);
        return new StoredScan(id, started, started.AddMinutes(1), "1.0.0", metadata, []);
    }

    private static MainWindow Open(TestServices services)
    {
#pragma warning disable CA2000 // MainWindow owns this view model and disposes it on Closed.
        var window = new MainWindow(new MainViewModel(services));
#pragma warning restore CA2000
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static T Find<T>(MainWindow window, string name) where T : Control
    {
        window.UpdateLayout();
        return window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    }

    private static string VisibleText(MainWindow window)
    {
        window.UpdateLayout();
        return string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
    }
}
