using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Platform;
using FaultWitness.Platform.Windows;
using FaultWitness.Rules;

namespace FaultWitness.UI.Tests;

/// <summary>Opt-in, read-only validation; only aggregate results are written outside the repository.</summary>
public sealed class WhatChangedLiveValidationTests
{
    [AvaloniaFact]
    public async Task RealMachineHistoryCollectsNormalizesAndDisplaysWithoutPersistingRecords()
    {
        if (Environment.GetEnvironmentVariable("FAULTWITNESS_LIVE_CHANGES") != "1") return;
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_CHANGE_VALIDATION_OUTPUT")
            ?? throw new InvalidOperationException("An external aggregate output path is required.");
        var now = DateTimeOffset.UtcNow;
        var token = TestContext.Current.CancellationToken;
        var events = await new WindowsDiagnosticsProvider().ReadAsync(now.AddDays(-7), now, token);
        var scan = new IncidentAnalyzer(RuleCatalog.CreateDefault()).Analyze(events, now.AddDays(-7), now, token);
        var provider = new MeasuredProvider();
        var result = await ChangeHistoryEnricher.EnrichAsync(scan, [], provider, token);
        // Never manufacture an onset or a machine incident when none was observed.
        var selected = result.Incidents.Where(item => item.ChangeContext is not null)
            .OrderByDescending(item => item.RelatedChanges.Count).FirstOrDefault();
        var displayedThemes = 0;
        if (selected is not null)
        {
            using var viewModel = new MainViewModel(new TestServices { Result = result });
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                viewModel.SetResult(result);
                viewModel.Select(viewModel.AllRows.Single(row => row.Incident.Id == selected.Id));
                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    viewModel.ChangeSettings(viewModel.Settings with { Theme = theme });
                    viewModel.Navigate(AppPage.Detail);
                    window.UpdateLayout();
                    foreach (var disclosure in window.GetVisualDescendants().OfType<Expander>().ToArray()) disclosure.IsExpanded = true;
                    window.UpdateLayout();
                    var text = string.Join(" ", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
                    Assert.True(text.Contains(viewModel.Text.Get("ChangesNearFirst"), StringComparison.Ordinal));
                    Assert.True(text.Contains(viewModel.Text.Get("ChangesDisclaimer"), StringComparison.Ordinal));
                    foreach (var related in selected.RelatedChanges)
                        Assert.True(text.Contains(SystemChangePrivacy.Sanitize(related.Change).Subject, StringComparison.Ordinal));
                    if (!selected.RelatedChanges.Any(item => item.Relevance != ContextualRelevance.Low))
                        Assert.True(text.Contains(viewModel.Text.Get("NoRelevantChanges"), StringComparison.Ordinal));
                    displayedThemes++;
                }
                var json = JsonSerializer.Serialize(ChangePresentation.ToJsonModel(selected, new ExportPrivacyOptions(false)));
                foreach (var identity in provider.Changes.Select(item => item.ComponentIdentity).Where(item => !string.IsNullOrEmpty(item)))
                {
                    Assert.False(json.Contains(identity!, StringComparison.OrdinalIgnoreCase));
                    Assert.False(json.Contains(JsonSerializer.Serialize(identity)[1..^1], StringComparison.OrdinalIgnoreCase));
                }
            }
            finally { window.Close(); }
        }
        var aggregate = new
        {
            CollectionMilliseconds = provider.CollectionMilliseconds,
            IncidentScanDays = 7,
            QueryCount = provider.QueryCount,
            ChangeCount = provider.Changes.DistinctBy(item => item.Id).Count(),
            Categories = provider.Changes.DistinctBy(item => item.Id).GroupBy(item => item.Category).ToDictionary(group => group.Key.ToString(), group => group.Count()),
            Sources = provider.Coverage.Select(item => new { item.Channel, State = item.State.ToString() }).Distinct(),
            IncidentCount = result.Incidents.Count,
            RelatedChangeCount = selected?.RelatedChanges.Count ?? 0,
            DisplayedThemes = displayedThemes,
            SyntheticDisplayAnchor = false,
            DeviceIdentifiersExcluded = true,
            PersistedMachineRecords = 0
        };
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(aggregate), token);
    }

    private sealed class MeasuredProvider : IChangeHistoryProvider
    {
        public double CollectionMilliseconds { get; private set; }
        public int QueryCount { get; private set; }
        public List<SystemChange> Changes { get; } = [];
        public List<SourceCoverage> Coverage { get; } = [];
        public async Task<ChangeHistoryBatch> GetChangesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            var batch = await new WindowsChangeHistoryProvider().GetChangesAsync(fromUtc, toUtc, cancellationToken);
            CollectionMilliseconds += watch.Elapsed.TotalMilliseconds;
            QueryCount++;
            Changes.AddRange(batch.Changes);
            Coverage.AddRange(batch.Coverage);
            return batch;
        }
    }
}
