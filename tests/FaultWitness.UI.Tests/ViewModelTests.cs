using System.Diagnostics;
using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Export;
using FaultWitness.Localization;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "ViewModel")]
public sealed class ViewModelTests
{
    [Fact]
    public void HtmlReport_EncodesUntrustedDiagnosticText()
    {
        var scan = SyntheticResults.Create(1); var incident = scan.Incidents[0];
        var source = incident.AnchorEvent with { Provider = "<script>untrusted</script>" };
        incident = incident with { Evidence = [new(EvidenceKind.Positive, "EventLog", "evidence.recorded", "synthetic", source)] };
        var html = ReportExporter.ToHtml(scan with { Incidents = [incident] }, new ExportPrivacyOptions());
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal); Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }
    [Fact]
    public void RecentSignificant_ShowsRepresentativeSignaturesWithoutMergingOccurrences()
    {
        var sample = SyntheticResults.Create(1).Incidents[0];
        var result = SyntheticResults.Create(0) with { Incidents = Enumerable.Range(0, 100).Select(index => sample with { Id = Guid.NewGuid(), StartTimeUtc = sample.StartTimeUtc.AddMinutes(index) }).ToArray() };
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(result);
        Assert.Single(vm.RecentSignificant); Assert.Equal(100, vm.AllRows.Count); Assert.Equal(100, vm.AttentionCount);
    }
    [Fact]
    public void ImportSelection_IsBoundedAcrossMultipleBrowseOperations()
    {
        using var vm = new MainViewModel(new TestServices());
        vm.AddImports(Enumerable.Range(0, 15).Select(index => index + ".wer"));
        vm.AddImports(Enumerable.Range(15, 15).Select(index => index + ".wer"));
        Assert.Equal(20, vm.Imports.Count); Assert.Equal("ImportLimit", vm.StatusKey);
    }
    [Fact]
    public async Task ImportAggregate_DoesNotExceedExistingEventLimit()
    {
        using var vm = new MainViewModel(new TestServices { Result = SyntheticResults.Create(11_000) });
        vm.AddImports(["one.wer", "two.wer"]); await vm.AnalyzeImportsAsync();
        Assert.Equal("ImportAccepted", vm.Imports[0].StatusKey); Assert.Equal("ImportRejected", vm.Imports[1].StatusKey);
    }
    [Fact]
    public async Task SourceFailure_KeepsPreviousResultAndShowsSafeError()
    {
        using var vm = new MainViewModel(new TestServices { FailAnalysis = true });
        var previous = SyntheticResults.Create(3); vm.SetResult(previous); await vm.AnalyzeAsync();
        Assert.Same(previous, vm.Result); Assert.False(vm.IsBusy); Assert.Equal("AnalysisError", vm.StatusKey);
        Assert.Equal("UnauthorizedAccessException", vm.TechnicalError); Assert.DoesNotContain("synthetic-private", vm.StatusText, StringComparison.Ordinal);
    }
    [Fact]
    public void Priority_UsesSignificantFindingsWithoutDeletingContext()
    {
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(1000));
        Assert.Equal(1, vm.AttentionCount); Assert.Equal(1, vm.KnowingCount); Assert.Equal(998, vm.BackgroundCount);
        Assert.Equal(1000, vm.AllRows.Count); Assert.Equal(2, vm.RecentSignificant.Count);
    }
    [Theory]
    [InlineData(FindingDisposition.Expected)]
    [InlineData(FindingDisposition.Suppressed)]
    [InlineData(FindingDisposition.Context)]
    [InlineData(FindingDisposition.Supporting)]
    public void ContextualHighSeverity_DoesNotBecomeAttention(FindingDisposition disposition)
    {
        var incident = SyntheticResults.Create(1).Incidents[0];
        incident = incident with { Findings = [incident.Findings[0] with { Disposition = disposition }] };
        Assert.Equal(AttentionLevel.Background, PresentationPolicy.Classify(incident));
    }
    [Theory]
    [InlineData(AttentionLevel.Attention, 1)]
    [InlineData(AttentionLevel.Knowing, 1)]
    [InlineData(AttentionLevel.Background, 98)]
    public void PriorityFilter_PreservesFullTechnicalResult(AttentionLevel level, int expected)
    {
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(100));
        vm.SetFilter(new(Priority: level)); Assert.Equal(expected, vm.FilteredRows.Count); Assert.Equal(100, vm.Result.Incidents.Count);
    }
    [Theory]
    [InlineData("searchtarget", 1)]
    [InlineData("example.dll", 10)]
    [InlineData("SyntheticProvider", 10)]
    [InlineData("synthetic-device", 10)]
    [InlineData("graphics.engine_timeout", 10)]
    [InlineData("timeout", 10)]
    [InlineData("missing", 0)]
    public void Search_MatchesUsefulIdentityAndAssessment(string search, int expected)
    {
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(10));
        vm.SetFilter(new(Search: search)); Assert.Equal(expected, vm.FilteredRows.Count);
    }
    [Fact]
    public void DateCategoryAndStrengthFilters_Compose()
    {
        using var vm = new MainViewModel(new TestServices()); var result = SyntheticResults.Create(10); vm.SetResult(result);
        vm.SetFilter(new(Category: IncidentCategory.Graphics, Strength: EvidenceStrength.Moderate, From: result.Incidents[1].StartTimeUtc));
        Assert.Single(vm.FilteredRows);
    }
    [Fact]
    public void LocalTime_UsesRequestedZoneAndPreservesInstant()
    {
        var utc = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var local = PresentationPolicy.LocalTime(utc, TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test"));
        Assert.Equal(14, local.Hour); Assert.Equal(utc, local.ToUniversalTime());
    }
    [Theory]
    [InlineData(AnalysisPeriod.Day, 1)]
    [InlineData(AnalysisPeriod.Week, 7)]
    [InlineData(AnalysisPeriod.Month, 30)]
    public async Task AnalysisPeriod_UsesSelectedDuration(AnalysisPeriod period, int days)
    {
        var service = new TestServices(); using var vm = new MainViewModel(service) { Period = period };
        await vm.AnalyzeAsync(); Assert.Equal(TimeSpan.FromDays(days), service.To - service.From); Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task AroundTime_UsesSymmetricSelectedWindow()
    {
        var service = new TestServices(); using var vm = new MainViewModel(service) { AroundTime = DateTimeOffset.Now.AddHours(-1), WindowMinutes = 5 };
        await vm.AnalyzeAsync(true); Assert.Equal(TimeSpan.FromMinutes(10), service.To - service.From); Assert.True(vm.IsAround);
    }
    [Fact]
    public async Task Cancellation_KeepsExistingResultsAndDoesNotSave()
    {
        var service = new TestServices { WaitForCancellation = true }; using var vm = new MainViewModel(service);
        var previous = SyntheticResults.Create(4); vm.SetResult(previous);
        var running = vm.AnalyzeAsync(); Assert.True(vm.IsBusy); vm.Cancel(); await running;
        Assert.Equal("AnalysisCancelled", vm.StatusKey); Assert.Same(previous, vm.Result); Assert.Equal(0, service.Saved);
    }
    [Fact]
    public async Task InvalidCustomInterval_DoesNotStartCollection()
    {
        var service = new TestServices(); using var vm = new MainViewModel(service) { Period = AnalysisPeriod.Custom, CustomFrom = DateTimeOffset.Now.AddDays(1) };
        await vm.AnalyzeAsync(); Assert.Equal("InvalidTimeRange", vm.StatusKey); Assert.Equal(0, service.Saved);
    }
    [Fact]
    public async Task HistoryFailure_DoesNotDiscardCompletedAnalysis()
    {
        var service = new TestServices { FailHistory = true }; using var vm = new MainViewModel(service);
        await vm.AnalyzeAsync(); Assert.True(vm.HasAnalysis); Assert.Equal("HistoryError", vm.StatusKey); Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task ImportedAnalysis_IsLabelledAndCoverageStaysPartial()
    {
        using var vm = new MainViewModel(new TestServices()); vm.AddImports(["synthetic.wer"]); await vm.AnalyzeImportsAsync();
        Assert.True(vm.IsImported); Assert.Equal("Imported diagnostic data", vm.Origin);
        Assert.All(vm.Result.Coverage, item => Assert.Equal(CoverageState.Partial, item.State));
    }
    [Fact]
    public async Task RejectedImport_IsFriendlyAndDoesNotCreateResult()
    {
        using var vm = new MainViewModel(new TestServices { RejectImport = true }); vm.AddImports(["broken.zip"]); await vm.AnalyzeImportsAsync();
        Assert.Equal("ImportRejected", vm.StatusKey); Assert.False(vm.HasAnalysis); Assert.Equal("ImportRejected", vm.Imports[0].StatusKey);
        Assert.DoesNotContain("Exception", vm.StatusText, StringComparison.Ordinal);
    }
    [Fact]
    public void StandaloneJson_IsNotAdvertisedAsSupportedImport()
    {
        using var vm = new MainViewModel(new TestServices()); vm.AddImports(["unsupported.json"]);
        Assert.Equal("ImportUnsupported", Assert.Single(vm.Imports).StatusKey);
    }
    [Fact]
    public void RecurringOccurrences_RemainSeparateAndNavigable()
    {
        using var vm = new MainViewModel(new TestServices()); vm.SetResult(SyntheticResults.Create(100));
        vm.Select(vm.AllRows.Single(row => row.RecurrenceCount == 2 && row.Priority == AttentionLevel.Attention)); vm.ViewOccurrences();
        Assert.Equal(2, vm.FilteredRows.Count); Assert.Equal(100, vm.AllRows.Count);
    }
    [Theory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void RuntimeLanguage_IsPersistedAndRebuildsAssessment(string language)
    {
        var service = new TestServices(); using var vm = new MainViewModel(service); vm.SetResult(SyntheticResults.Create(3));
        vm.ChangeSettings(vm.Settings with { Language = language });
        Assert.Equal(language, vm.Text.Culture.Name); Assert.Equal(language, service.Settings.Language);
        Assert.Equal(vm.Text.Get("rule.graphics.engine_timeout.observed"), vm.AllRows[0].Assessment);
    }
    [Theory]
    [InlineData(AppTheme.System)][InlineData(AppTheme.Light)][InlineData(AppTheme.Dark)]
    public void ThemeSelection_IsPersisted(AppTheme theme)
    {
        var service = new TestServices(); using var vm = new MainViewModel(service); vm.ChangeSettings(vm.Settings with { Theme = theme });
        Assert.Equal(theme, service.Settings.Theme);
    }
    [Fact]
    public async Task ClearHistory_UsesOnlyApplicationStoreAndResetsMemory()
    {
        var service = new TestServices(); using var vm = new MainViewModel(service); vm.SetResult(SyntheticResults.Create(3));
        await vm.ClearDataAsync(); Assert.Equal(1, service.Cleared); Assert.False(vm.HasAnalysis); Assert.Empty(vm.AllRows);
    }
    [Fact]
    public void SupportSummary_PreservesEvidenceContractsAndVersionsWithoutPrivateRawData()
    {
        var text = ReportExporter.ToSupportMarkdown(SyntheticResults.Create(1), new LocalizationService(), true, "0.9.0-private", "0.9.1");
        Assert.Contains("Observed", text, StringComparison.Ordinal); Assert.Contains("Not observed", text, StringComparison.Ordinal);
        Assert.Contains("Unknown", text, StringComparison.Ordinal); Assert.Contains("What we cannot conclude", text, StringComparison.Ordinal);
        Assert.Contains("Best next step", text, StringComparison.Ordinal); Assert.Contains("0.9.1", text, StringComparison.Ordinal);
        Assert.Contains("Imported diagnostic data", text, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private", text, StringComparison.Ordinal); Assert.DoesNotContain("<Event>", text, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(100)][InlineData(1000)][InlineData(5000)]
    [Trait("Suite", "LargeData")]
    public void LargeResultSet_PrioritizesAndFiltersWithoutPathologicalWork(int count)
    {
        using var vm = new MainViewModel(new TestServices()); var result = SyntheticResults.Create(count); var clock = Stopwatch.StartNew();
        vm.SetResult(result);
        for (var index = 0; index < 25; index++) vm.SetFilter(new(Search: index % 2 == 0 ? "SearchTarget" : "Example"));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{count} records: {clock.Elapsed.TotalMilliseconds} ms");
        Assert.Equal(2, vm.RecentSignificant.Count); Assert.Equal(count, vm.AllRows.Count);
    }
}
