using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "WhatChanged")]
public sealed class WhatChangedPresentationTests
{
    [AvaloniaFact]
    public void Detail_ShowsChangesSectionRowsLowDisclosureAfterMarkerAndCoverage()
    {
        var baseResult = SyntheticResults.Create(1); var incident = baseResult.Incidents[0];
        var stamp = incident.StartTimeUtc;
        var change = new SystemChange("c", "Windows", stamp.AddMinutes(2), ChangeCategory.DriverInstalled, "Updates", "Display driver", ChangeSubsystem.Display, "old", "new", "Vendor", "ref");
        var result = baseResult with
        {
            Incidents = [incident with
            {
                RelatedChanges = [new RelatedSystemChange(change, ContextualRelevance.High, ChangeTiming.Before, TimeSpan.FromMinutes(-2), "r"), new RelatedSystemChange(change with { Id = "low", Subject = "Low detail" }, ContextualRelevance.Low, ChangeTiming.After, TimeSpan.FromMinutes(2), "r")],
                ChangeContext = new ChangeHistoryContext(stamp, FirstObservationBasis.CurrentScan, false, [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Partial, stamp.AddDays(-1), stamp, "source-detail", "Updates")])
            }]
        };
        using var viewModel = new MainViewModel(new TestServices { Result = result }); var window = new MainWindow(viewModel); window.Show();
        try
        {
            window.ViewModel.SetResult(result); window.ViewModel.Select(window.ViewModel.AllRows[0]); window.ViewModel.Navigate(AppPage.Detail); window.UpdateLayout();
            var text = VisibleText(window);
            Assert.Contains(window.ViewModel.Text.Get("ChangesNearFirst"), text, StringComparison.Ordinal);
            Assert.Contains("Updates", text, StringComparison.Ordinal);
            Assert.Contains(window.ViewModel.Text.Get("TechnicalDetails"), text, StringComparison.Ordinal);
            Assert.Contains(window.ViewModel.Text.Get("ChangesCoverageHelp"), text, StringComparison.Ordinal);
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
            window.UpdateLayout(); Assert.Contains(window.ViewModel.Text.Get("ChangeTimingAfter"), VisibleText(window), StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Detail_WithoutContextSaysNotExamined()
    {
        using var viewModel = new MainViewModel(new TestServices()); var window = new MainWindow(viewModel); window.Show();
        try { window.ViewModel.SetResult(SyntheticResults.Create(1)); window.ViewModel.Select(window.ViewModel.AllRows[0]); window.ViewModel.Navigate(AppPage.Detail); window.UpdateLayout(); Assert.Contains(window.ViewModel.Text.Get("ChangesNotExamined"), VisibleText(window), StringComparison.Ordinal); }
        finally { window.Close(); }
    }

    private static string VisibleText(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
}
