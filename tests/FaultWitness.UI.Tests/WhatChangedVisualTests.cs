using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "WhatChangedVisual")]
public sealed class WhatChangedVisualTests
{
    [AvaloniaFact]
    public void IncidentDetail_WhatChangedStatesRenderInLightAndDarkThemes()
    {
        var baseResult = SyntheticResults.Create(1); var incident = baseResult.Incidents[0];
        var stamp = incident.StartTimeUtc;
        var change = new SystemChange("high", "Windows", stamp.AddMinutes(-3), ChangeCategory.DriverInstalled, "ChangeSourceSetupApi", "Display driver", ChangeSubsystem.Display, "1", "2", "Vendor", "reference");
        var populated = incident with
        {
            RelatedChanges = [new RelatedSystemChange(change, ContextualRelevance.High, ChangeTiming.Before, TimeSpan.FromMinutes(-3), "ChangeReasonSameSubsystem"), new RelatedSystemChange(change with { Id = "low", TimestampUtc = stamp.AddMinutes(2) }, ContextualRelevance.Low, ChangeTiming.After, TimeSpan.FromMinutes(2), "ChangeReasonAfter")],
            ChangeContext = new ChangeHistoryContext(stamp, FirstObservationBasis.CurrentScan, false, [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Partial, stamp.AddDays(-1), stamp, "ChangeCoveragePartial", "ChangeSourceSetupApi")])
        };
        var scenarios = new[]
        {
            ("populated", populated),
            ("complete-empty", incident with { ChangeContext = new ChangeHistoryContext(stamp, FirstObservationBasis.CurrentScan, false, [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Complete, stamp.AddDays(-1), stamp, "ChangeCoveragePartial", "ChangeSourceSetupApi")]) }),
            ("partial-empty", incident with { ChangeContext = new ChangeHistoryContext(stamp, FirstObservationBasis.CurrentScan, false, [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Partial, stamp.AddDays(-1), stamp, "ChangeCoveragePartial", "ChangeSourceSetupApi")]) }),
            ("unavailable-empty", incident with { ChangeContext = new ChangeHistoryContext(stamp, FirstObservationBasis.CurrentScan, false, [new SourceCoverage(SourceType.ChangeHistory, CoverageState.Unavailable, null, null, "ChangeCoveragePartial", "ChangeSourceSetupApi")]) })
        };
        using var viewModel = new MainViewModel(new TestServices()); var window = new MainWindow(viewModel); window.Show();
        try
        {
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            foreach (var (name, item) in scenarios)
            {
                viewModel.ChangeSettings(viewModel.Settings with { Theme = theme }); viewModel.SetResult(baseResult with { Incidents = [item] }); viewModel.Select(viewModel.AllRows[0]); viewModel.Navigate(AppPage.Detail); window.Width = 1100; window.Height = 800; window.UpdateLayout();
                var method = typeof(MainWindow).GetMethod("ChangesSection", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var section = (Control)method.Invoke(window, [item])!;
                window.Content = new ScrollViewer { Content = new Border { Padding = new Thickness(24), Child = section } };
                window.UpdateLayout();
                var visible = VisibleText(window);
                Assert.Contains(viewModel.Text.Get("ChangesNearFirst"), visible, StringComparison.Ordinal);
                Assert.True(window.GetVisualDescendants().OfType<ScrollViewer>().All(view => view.Extent.Width <= view.Viewport.Width + 2 || view.Viewport.Width == 0), name + "/" + theme);
                if (name == "populated")
                {
                    foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
                    window.UpdateLayout(); visible = VisibleText(window);
                    Assert.Contains(viewModel.Text.Get("ChangeTimingAfter"), visible, StringComparison.Ordinal);
                    Assert.Contains("Temporal proximity does not establish causation.", visible, StringComparison.Ordinal);
                    Assert.Contains(viewModel.Text.Get("ChangeSourceSetupApi"), visible, StringComparison.Ordinal);
                }
                var output = Environment.GetEnvironmentVariable("FAULTWITNESS_CHANGE_VISUAL_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var directory = output; Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The real Avalonia view did not render.");
                    frame.Save(Path.Combine(directory, $"what-changed-{name}-{theme.ToString().ToLowerInvariant()}.png"), new PngBitmapEncoderOptions());
                }
            }
        }
        finally { window.Close(); }

    }

    private static string VisibleText(Control root) => string.Join(" ", root.GetVisualDescendants().OfType<TextBlock>().Where(item => item.IsVisible).Select(item => item.Text));
}
