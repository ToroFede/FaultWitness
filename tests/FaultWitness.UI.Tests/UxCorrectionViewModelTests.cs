using FaultWitness.App;
using FaultWitness.Core;
using FaultWitness.Localization;
using FaultWitness.Storage;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "ViewModel")]
public sealed class UxCorrectionViewModelTests
{
    [Theory]
    [InlineData("FaultWitness.UI.Tests.exe", true)]
    [InlineData(@"C:\synthetic\faultwitness.ui.tests.EXE", true)]
    [InlineData("FaultWitness.App.exe", false)]
    [InlineData("FaultWitness.UI.Tests.exe.other", false)]
    [InlineData("Unrelated.Tests.exe", false)]
    [InlineData(null, false)]
    public void DevelopmentFilenameHint_IsExactAndDoesNotLabelOrdinaryApplication(string? process, bool expected)
        => Assert.Equal(expected, PresentationPolicy.HasDevelopmentExecutableName(process));

    [Fact]
    public void DevelopmentCrash_RemainsVisibleWithUnchangedEvidenceAndPriority()
    {
        var incident = SyntheticResults.Incident(0, DateTimeOffset.UtcNow);
        var original = new IncidentRow(incident, new LocalizationService(), 1);
        var development = incident with { AnchorEvent = incident.AnchorEvent with { Process = "FaultWitness.UI.Tests.exe" } };
        using var vm = new MainViewModel(new TestServices());
        vm.SetResult(new ScanResult([development], [], incident.StartTimeUtc, incident.EndTimeUtc));
        var shown = Assert.Single(vm.AllRows);
        Assert.Equal(original.Priority, shown.Priority);
        Assert.Same(development, shown.Incident);
        Assert.Same(incident.Findings, shown.Incident.Findings);
        Assert.Same(incident.Evidence, shown.Incident.Evidence);
        Assert.Equal(vm.Text.Get("DevelopmentProcessContext"), shown.DevelopmentContext);
        Assert.Contains("crash evidence is retained", shown.DevelopmentContext, StringComparison.Ordinal);
        var application = new IncidentRow(incident with { AnchorEvent = incident.AnchorEvent with { Process = "FaultWitness.App.exe" } }, vm.Text, 1);
        Assert.Empty(application.DevelopmentContext);
        Assert.Equal(original.Priority, application.Priority);
        Assert.Single(vm.RecentSignificant);
    }

    [Fact]
    public async Task ReadinessCompletion_DoesNotFollowUserIntoSettings()
    {
        using var vm = new MainViewModel(new TestServices());
        vm.Navigate(AppPage.Readiness);
        await vm.RefreshReadinessAsync();
        Assert.True(vm.HasVisibleStatus);
        vm.Navigate(AppPage.Settings);
        Assert.False(vm.HasVisibleStatus);
        Assert.Empty(vm.TechnicalError);
    }

    [Fact]
    public async Task RunningAnalysis_CanBeCancelledAfterNavigatingElsewhere()
    {
        using var vm = new MainViewModel(new TestServices { WaitForCancellation = true });
        vm.OpenAnalyze(AnalysisMode.Recent);
        var operation = vm.AnalyzeAsync();
        vm.Navigate(AppPage.Settings);
        Assert.True(vm.HasVisibleStatus);
        vm.Cancel(); await operation;
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasVisibleStatus);
        Assert.Equal("AnalysisCancelled", vm.StatusKey);
    }

    [Fact]
    public async Task HistoryRefresh_RebindsSelectionToCurrentRowsAndClearsMissingSelection()
    {
        var now = DateTimeOffset.UtcNow;
        var services = new TestServices { History = [new StoredScan("first", now, now, "test", null, []), new StoredScan("second", now, now, "test", null, [])] };
        using var vm = new MainViewModel(services);
        await vm.RefreshHistoryAsync(); vm.SelectHistory(vm.History[1]);
        await vm.RefreshHistoryAsync();
        Assert.Same(vm.History[1], vm.SelectedHistory);
        services.History = [];
        await vm.RefreshHistoryAsync();
        Assert.Null(vm.SelectedHistory);
    }
}
