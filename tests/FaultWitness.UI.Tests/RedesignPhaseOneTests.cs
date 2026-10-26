using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media.Imaging;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Presentation;
using FaultWitness.App.Views.Components;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;

#pragma warning disable CA2000 // Window.Closed owns the view model lifetime, including animation cancellation.
namespace FaultWitness.UI.Tests;

public sealed class RedesignPhaseOneTests
{
    [Fact]
    public void PresentationPreferencesAreBackwardCompatibleAndRoundTrip()
    {
        var previous = JsonSerializer.Deserialize<UserSettings>("{\"Language\":\"it\",\"Theme\":2}")!;
        Assert.False(previous.ReduceMotion);
        Assert.False(previous.UseSystemBackdrop);
        var changed = previous with { ReduceMotion = true, UseSystemBackdrop = true };
        Assert.Equal(changed, JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(changed)));
    }

    [AvaloniaFact]
    public void PreferencesPersistAndApplyToRealDerivedWindowAndButtonTemplates()
    {
        var services = new TestServices();
        var vm = new MainViewModel(services);
        var window = new MainWindow(vm);
        window.Show();
        try
        {
            var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryAnalyze");
            var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            Assert.Contains(presenter.Transitions!, t => t is BrushTransition transition && transition.Duration == TimeSpan.FromMilliseconds(140));
            vm.OpenAnalyze(AnalysisMode.Recent);
            window.UpdateLayout();
            Assert.IsType<CrossFade>(window.GetVisualDescendants().OfType<AnalyzeView>().Single().FindControl<Expander>("AdvancedSources")!.ContentTransition);
            vm.Navigate(AppPage.Settings);
            window.UpdateLayout();
            var setting = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ReduceMotionPreference");
            setting.SetCurrentValue(ToggleButtonIsChecked(), true);
            Assert.True(services.Settings.ReduceMotion);
            Assert.Contains("reduced-motion", window.Classes);
            vm.OpenAnalyze(AnalysisMode.Recent);
            window.UpdateLayout();
            Assert.Null(window.GetVisualDescendants().OfType<AnalyzeView>().Single().FindControl<Expander>("AdvancedSources")!.ContentTransition);
            vm.Navigate(AppPage.Home);
            window.UpdateLayout();
            primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryAnalyze");
            presenter = primary.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            Assert.True(presenter.Transitions is null || presenter.Transitions.Count == 0);
            vm.ChangeSettings(vm.Settings with { UseSystemBackdrop = true });
            Assert.Equal(new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None }, window.TransparencyLevelHint);
            Assert.Equal(WindowTransparencyLevel.None, window.ActualTransparencyLevel);
            Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color.A);
        }
        finally { window.Close(); }
    }

    private static StyledProperty<bool?> ToggleButtonIsChecked() => Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty;

    [AvaloniaTheory]
    [InlineData(560, AppTheme.Dark, "de")]
    [InlineData(640, AppTheme.Light, "ru")]
    [InlineData(1280, AppTheme.Dark, "it")]
    [InlineData(1280, AppTheme.System, "en")]
    public void RedesignedPagesKeepFactsAndStayWithinViewport(int width, AppTheme theme, string language)
    {
        var vm = new MainViewModel(new TestServices { Settings = new UserSettings(Language: language, Theme: theme, ReduceMotion: true) });
        var window = new MainWindow(vm);window.Show();window.Width=width;window.Height=900;
        try
        {
            vm.SetResult(SyntheticResults.Create(3));
            foreach (var page in new[] { AppPage.Home, AppPage.Analyze, AppPage.Detail })
            {
                if (page==AppPage.Detail)vm.Select(vm.AllRows[0]); else vm.Navigate(page);
                window.UpdateLayout();
                Assert.Single(window.GetVisualDescendants(), c=>c is HomeView or AnalyzeView or IncidentDetailView);
                Assert.All(window.GetVisualDescendants().OfType<ScrollViewer>().Where(v=>v.Viewport.Width>0),v=>Assert.True(v.Extent.Width<=v.Viewport.Width+2,$"{page}/{width}/{language}"));
                Assert.NotEmpty(window.GetVisualDescendants().OfType<SurfaceCard>());
            }
            var projection=(IncidentDetailPresentation)window.GetVisualDescendants().OfType<IncidentDetailView>().Single().DataContext!;
            var texts=window.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
            Assert.All(projection.Limitations,t=>Assert.Contains(t,texts));
            Assert.Contains(projection.ObservedSummary,texts);
            Assert.Equal(1, AutomationProperties.GetHeadingLevel(window.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Classes.Contains("page-title"))));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetricCardsKeepTheirThemeAfterCachedNavigationAndMotionChanges(bool reduced)
    {
        var vm=new MainViewModel(new TestServices { Settings=new UserSettings(Theme:AppTheme.Dark) });
        var window=new MainWindow(vm);window.Show();
        try
        {
            vm.SetResult(SyntheticResults.Create(3));
            vm.Navigate(AppPage.Settings);window.UpdateLayout();
            vm.ChangeSettings(vm.Settings with { Theme=AppTheme.Light,ReduceMotion=reduced });
            vm.Navigate(AppPage.Home);window.UpdateLayout();
            var home=window.GetVisualDescendants().OfType<HomeView>().Single();
            Assert.Contains("redesigned",home.Classes);
            var metric=home.FindControl<Button>("CountKnowing")!;
            Assert.Equal(96,metric.MinHeight);
            Assert.Equal(new CornerRadius(16),metric.CornerRadius);
            Assert.Equal(new Thickness(20),metric.Padding);
            Assert.IsType<MetricCard>(metric);
            var presenter=metric.GetVisualDescendants().OfType<ContentPresenter>().Single(c=>c.Name=="PART_ContentPresenter");
            Assert.Equal(metric.CornerRadius,presenter.CornerRadius);
            Assert.Equal(metric.Padding,presenter.Padding);
            Assert.Equal(Colors.White,Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void RealAvaloniaVisualRasterizesAtMultipleDpiWithoutChangingEvidence(double scale)
    {
        var vm=new MainViewModel(new TestServices { Settings=new UserSettings(ReduceMotion:true) });
        var window=new MainWindow(vm);window.Show();window.Width=1280;window.Height=900;
        try
        {
            vm.SetResult(IncidentDetailDesignData.Result);vm.Select(vm.AllRows[0]);window.UpdateLayout();
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)(1280*scale),(int)(900*scale)),new Vector(96*scale,96*scale));
            bitmap.Render(window);
            Assert.Equal((int)(1280*scale),bitmap.PixelSize.Width);
            Assert.Equal(96*scale,bitmap.Dpi.X);
            Assert.Equal(IncidentDetailDesignData.Result.Incidents[0].Id,vm.AllRows[0].Incident.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotionKeepsRealRunningFeedbackAndCancellationWithoutLoopingProgress()
    {
        var services=new TestServices { WaitForCancellation=true, Settings=new UserSettings(ReduceMotion:true) };
        var vm=new MainViewModel(services);var window=new MainWindow(vm);window.Show();
        try
        {
            vm.OpenAnalyze(AnalysisMode.Recent);
            var page=window.GetVisualDescendants().OfType<AnalyzeView>().Single();
            page.FindControl<Button>("RunAnalysis")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(var n=0;n<100 && !vm.IsBusy;n++)await Task.Delay(5);
            Assert.True(vm.IsBusy);
            Assert.False(page.FindControl<ProgressBar>("AnalyzeLocalProgress")!.IsVisible);
            Assert.True(page.FindControl<StackPanel>("AnalyzeFeedbackRegion")!.IsVisible);
            Assert.True(page.FindControl<Button>("CancelAnalyzeLocal")!.IsVisible);
            page.FindControl<Button>("CancelAnalyzeLocal")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for(var n=0;n<100 && vm.IsBusy;n++)await Task.Delay(5);
            Assert.False(vm.IsBusy);Assert.Equal("AnalysisCancelled",vm.StatusKey);
        }
        finally { vm.Cancel();window.Close(); }
    }
}
