using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FaultWitness.App;
using FaultWitness.App.Views.Pages;
using FaultWitness.Core;
using FaultWitness.Design;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "DialogConsistency")]
public sealed class DialogConsistencyTests
{
    private static readonly string[] Languages = ["en", "de", "ru", "pl"];

    [AvaloniaFact]
    public async Task OwnedConfirmationDialogs_RenderThroughProductionPathsInAllThemesAndLocales()
    {
        var output = Environment.GetEnvironmentVariable("FAULTWITNESS_PASS2D01_VISUAL_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);

        foreach (var language in Languages)
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        foreach (var kind in new[] { "clear", "configure", "restore" })
            await RenderProductionPath(kind, language, theme, output);

        foreach (var kind in new[] { "clear", "configure", "restore" })
            await RenderPreparedSystemTheme(kind, output);

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            RenderOrdinaryHome(theme, output);
    }

    [AvaloniaTheory]
    [InlineData(AppTheme.System, "clear")]
    [InlineData(AppTheme.System, "configure")]
    [InlineData(AppTheme.System, "restore")]
    [InlineData(AppTheme.Light, "clear")]
    [InlineData(AppTheme.Light, "configure")]
    [InlineData(AppTheme.Light, "restore")]
    [InlineData(AppTheme.Dark, "clear")]
    [InlineData(AppTheme.Dark, "configure")]
    [InlineData(AppTheme.Dark, "restore")]
    public async Task PreparedOwnedDialog_FollowsOwnerRequestedAndEffectiveTheme(AppTheme theme, string kind)
    {
        var owner = OpenOwner("en", theme);
        try
        {
            var dialog = PrepareDialog(kind, owner);
            var result = dialog.ShowDialog<bool>(owner);
            Settle(owner, dialog);

            Assert.Equal(owner.RequestedThemeVariant, dialog.RequestedThemeVariant);
            Assert.Equal(owner.ActualThemeVariant, dialog.ActualThemeVariant);
            Assert.Equal(owner.ViewModel.Text.Get(kind switch
            {
                "clear" => "ClearData",
                "configure" => "CaptureConfigure",
                _ => "CaptureRestore"
            }), dialog.Title);

            Find<Button>(dialog, CancelButton(kind)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(await result);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("clear")]
    [InlineData("configure")]
    [InlineData("restore")]
    public async Task OwnedDialog_ContainsTabFocusAndEscapeReturnsCancel(string kind)
    {
        var owner = OpenOwner("en", AppTheme.Light);
        try
        {
            var ownerFocus = Find<Button>(owner, "PrimaryAnalyze");
            Assert.True(ownerFocus.Focus(NavigationMethod.Tab));
            var dialog = PrepareDialog(kind, owner);
            var result = dialog.ShowDialog<bool>(owner);
            Settle(owner, dialog);

            var confirm = Find<Button>(dialog, ConfirmButton(kind));
            var cancel = Find<Button>(dialog, CancelButton(kind));
            Assert.True(confirm.Focus(NavigationMethod.Tab));
            SendKey(dialog, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.True(cancel.IsKeyboardFocusWithin, $"Tab did not move focus to {cancel.Name}.");
            SendKey(dialog, Key.Escape, PhysicalKey.Escape, null);
            Settle(owner, dialog);

            Assert.False(await result);
            Assert.True(ownerFocus.IsKeyboardFocusWithin, "Closing the owned modal did not return focus to the owner control.");
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void DestructiveStateChangeStyling_WinsAcrossInteractiveStates(string theme)
    {
        var variant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var button = new Button { Content = "Clear history", Width = 220, Height = 48 };
        button.Classes.Add("danger-action");
        button.Classes.Add("state-change-action");
        var window = new Window { Width = 400, Height = 200, Content = button, RequestedThemeVariant = variant };
        window.Show();
        window.UpdateLayout();
        try
        {
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter");
            var error = SemanticColor(theme, "error");
            AssertBrushColor(presenter.BorderBrush, error, "default destructive border");
            AssertBrushColor(presenter.Foreground, error, "default destructive text");

            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            Assert.True(button.Focus(NavigationMethod.Tab));
            window.UpdateLayout();
            AssertBrushColor(presenter.BorderBrush, error, "focused destructive border");

            window.MouseMove(point);
            window.UpdateLayout();
            AssertBrushColor(presenter.BorderBrush, error, "hovered destructive border");
            AssertBrushColor(presenter.Foreground, error, "hovered destructive text");

            window.MouseDown(point, MouseButton.Left);
            window.UpdateLayout();
            AssertBrushColor(presenter.BorderBrush, error, "pressed destructive border");
            AssertBrushColor(presenter.Foreground, error, "pressed destructive text");
            window.MouseUp(point, MouseButton.Left);

            button.IsEnabled = false;
            window.UpdateLayout();
            AssertBrushColor(presenter.BorderBrush, error, "disabled destructive border");
            AssertBrushColor(presenter.Foreground, SemanticColor(theme, "textMuted"), "disabled destructive text");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("pl")]
    public void DialogActionRows_AreBottomLeftAndShareSpacing(string language)
    {
        var owner = OpenOwner(language, AppTheme.Dark);
        try
        {
            var clear = PrepareDialog("clear", owner);
            var configure = PrepareDialog("configure", owner);
            var restore = PrepareDialog("restore", owner);
            foreach (var dialog in new Window[] { clear, configure, restore }) dialog.Show();
            try
            {
                foreach (var dialog in new Window[] { clear, configure, restore })
                {
                    dialog.UpdateLayout();
                    var grid = Assert.IsType<Grid>(dialog.Content);
                    var row = dialog.GetVisualDescendants().OfType<WrapPanel>().Single();
                    Assert.Equal(1, Grid.GetRow(row));
                    Assert.Equal(D("primitive.space.4"), grid.RowSpacing);
                    Assert.Equal(HorizontalAlignment.Stretch, row.HorizontalAlignment);
                    var buttons = row.Children.OfType<Button>().ToArray();
                    Assert.Equal(2, buttons.Length);
                    Assert.True(Math.Abs(buttons[0].Bounds.X) < 0.01, $"First action in {dialog.Title} did not start at the row's left edge: {buttons[0].Bounds}.");
                    Assert.True(buttons.All(button => Math.Abs(button.Bounds.Y - buttons[0].Bounds.Y) <= 3),
                        $"{dialog.Title}: action buttons wrapped: {string.Join("; ", buttons.Select(button => $"{button.Name}={button.Bounds}"))}; row={row.Bounds}.");
                }

                Assert.Equal(["ConfirmClear", "CancelClear"], clear.GetVisualDescendants().OfType<WrapPanel>().Single().Children.OfType<Button>().Select(button => button.Name));
                Assert.Equal(["CaptureConfirmButton", "CaptureCancelButton"], configure.GetVisualDescendants().OfType<WrapPanel>().Single().Children.OfType<Button>().Select(button => button.Name));
                Assert.Equal(["CaptureConfirmButton", "CaptureCancelButton"], restore.GetVisualDescendants().OfType<WrapPanel>().Single().Children.OfType<Button>().Select(button => button.Name));

                var clearConfirm = Find<Button>(clear, "ConfirmClear");
                Assert.Contains("danger-action", clearConfirm.Classes);
                Assert.Contains("state-change-action", clearConfirm.Classes);
                Assert.DoesNotContain("danger-action", Find<Button>(configure, "CaptureConfirmButton").Classes);
                var configureConfirm = Find<Button>(configure, "CaptureConfirmButton");
                var restoreConfirm = Find<Button>(restore, "CaptureConfirmButton");
                Assert.Contains("primary-action", configureConfirm.Classes);
                Assert.DoesNotContain("state-change-action", configureConfirm.Classes);
                Assert.Contains("secondary-action", restoreConfirm.Classes);
                Assert.Contains("state-change-action", restoreConfirm.Classes);
                Assert.DoesNotContain("danger-action", restoreConfirm.Classes);
                Assert.False(clearConfirm.IsDefault);
                Assert.False(configureConfirm.IsDefault);
                Assert.False(restoreConfirm.IsDefault);
                Assert.True(Find<Button>(clear, "CancelClear").IsCancel);
                Assert.True(Find<Button>(configure, "CaptureCancelButton").IsCancel);
                Assert.True(Find<Button>(restore, "CaptureCancelButton").IsCancel);
            }
            finally
            {
                clear.Close(); configure.Close(); restore.Close();
            }
        }
        finally { owner.Close(); }
    }

    private static async Task RenderProductionPath(string kind, string language, AppTheme theme, string? output)
    {
        var captureService = new CaptureAxamlTests.Service();
        var journal = new CaptureAxamlTests.Journal();
        var flow = kind == "clear" ? null : new CaptureWorkflow(captureService, journal) { TargetExecutable = "synthetic.exe" };
        if (kind == "restore")
        {
            await flow!.PreviewAsync();
            await flow.ConfigureAsync();
        }

        var services = new TestServices { Settings = new UserSettings(Language: language, Theme: theme), Capture = flow };
        var owner = OpenOwner(services);
        try
        {
            if (kind == "clear")
            {
                owner.ViewModel.Navigate(AppPage.Settings);
                Settle(owner);
                Find<Button>(owner, "ClearData").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                owner.ViewModel.Navigate(AppPage.Capture);
                Settle(owner);
                if (kind == "configure")
                {
                    CaptureAxamlTests.Click(owner, "CaptureReadButton");
                    CaptureAxamlTests.Find<CheckBox>(owner, "CaptureArchitectureConfirmation").IsChecked = true;
                    CaptureAxamlTests.Click(owner, "CaptureConfigureButton");
                }
                else
                {
                    var entry = Assert.Single(flow!.Entries);
                    CaptureAxamlTests.Click(owner, "CaptureRestoreButton" + entry.ActionId.ToString("N"));
                }
            }

            var dialog = Assert.IsAssignableFrom<Window>(Assert.Single(owner.OwnedWindows));
            Settle(owner, dialog);
            Assert.Equal(owner.ActualThemeVariant, dialog.ActualThemeVariant);
            AssertLocalizedActions(dialog, owner, kind);
            Assert.Equal(kind switch { "clear" => 480, _ => 500 }, (int)dialog.Bounds.Width);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{kind}/{language}/{theme} dialog did not render.");
            Assert.Equal((int)dialog.Bounds.Width, frame.PixelSize.Width);
            Assert.Equal((int)dialog.Bounds.Height, frame.PixelSize.Height);
            if (!string.IsNullOrWhiteSpace(output))
                frame.Save(Path.Combine(output, $"dialog-{kind}-{language}-{theme.ToString().ToLowerInvariant()}.png"), new PngBitmapEncoderOptions());

            var beforeExecutions = captureService.Executions;
            dialog.Close(false);
            Dispatcher.UIThread.RunJobs();
            if (kind == "clear") Assert.Equal(0, services.Cleared);
            else Assert.Equal(beforeExecutions, captureService.Executions);
        }
        finally { owner.Close(); }
    }

    private static async Task RenderPreparedSystemTheme(string kind, string? output)
    {
        var owner = OpenOwner("en", AppTheme.System);
        try
        {
            var dialog = PrepareDialog(kind, owner);
            var result = dialog.ShowDialog<bool>(owner);
            Settle(owner, dialog);
            Assert.Equal(ThemeVariant.Default, owner.RequestedThemeVariant);
            Assert.Equal(owner.ActualThemeVariant, dialog.ActualThemeVariant);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{kind}/system dialog did not render.");
            Assert.Equal((int)dialog.Bounds.Width, frame.PixelSize.Width);
            Assert.Equal((int)dialog.Bounds.Height, frame.PixelSize.Height);
            if (!string.IsNullOrWhiteSpace(output))
                frame.Save(Path.Combine(output, $"dialog-{kind}-en-system.png"), new PngBitmapEncoderOptions());
            dialog.Close(false);
            Assert.False(await result);
        }
        finally { owner.Close(); }
    }

    private static void RenderOrdinaryHome(AppTheme theme, string? output)
    {
        var owner = OpenOwner("en", theme);
        try
        {
            owner.ViewModel.SetResult(SyntheticResults.Create(3));
            Settle(owner);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            using var frame = owner.CaptureRenderedFrame() ?? throw new InvalidOperationException($"Home/{theme} did not render.");
            Assert.Equal(900, frame.PixelSize.Width);
            Assert.Equal(700, frame.PixelSize.Height);
            if (!string.IsNullOrWhiteSpace(output))
                frame.Save(Path.Combine(output, $"home-en-{theme.ToString().ToLowerInvariant()}.png"), new PngBitmapEncoderOptions());
        }
        finally { owner.Close(); }
    }

    private static ClearDataConfirmationWindow PrepareClear(MainWindow owner)
    {
        var dialog = new ClearDataConfirmationWindow();
        dialog.Prepare(owner.ViewModel.Text, owner);
        return dialog;
    }

    private static Window PrepareDialog(string kind, MainWindow owner) => kind switch
    {
        "clear" => PrepareClear(owner),
        "configure" => PrepareCapture(owner, false),
        "restore" => PrepareCapture(owner, true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown dialog kind.")
    };

    private static CaptureConfirmationWindow PrepareCapture(MainWindow owner, bool restore)
    {
        var dialog = new CaptureConfirmationWindow();
        dialog.Prepare(owner.ViewModel.Text, owner, restore);
        return dialog;
    }

    private static MainWindow OpenOwner(string language, AppTheme theme, CaptureWorkflow? capture = null) =>
        OpenOwner(new TestServices { Settings = new UserSettings(Language: language, Theme: theme), Capture = capture });

    private static MainWindow OpenOwner(TestServices services)
    {
#pragma warning disable CA2000 // MainWindow takes ownership of the view model and disposes it when closed.
        var viewModel = new MainViewModel(services);
        var owner = new MainWindow(viewModel);
#pragma warning restore CA2000
        VisualRenderGeometry.ShowAtRequestedGeometry(owner, 900, 700);
        return owner;
    }

    private static void AssertLocalizedActions(Window dialog, MainWindow owner, string kind)
    {
        var confirm = Find<Button>(dialog, ConfirmButton(kind));
        var cancel = Find<Button>(dialog, CancelButton(kind));
        var expectedConfirm = owner.ViewModel.Text.Get(kind switch
        {
            "clear" => "ConfirmClear",
            "configure" => "CaptureConfirmConfigure",
            _ => "CaptureConfirmRestore"
        });
        Assert.Equal(expectedConfirm, AutomationProperties.GetName(confirm));
        Assert.Equal(owner.ViewModel.Text.Get("Cancel"), AutomationProperties.GetName(cancel));
    }

    private static string ConfirmButton(string kind) => kind == "clear" ? "ConfirmClear" : "CaptureConfirmButton";
    private static string CancelButton(string kind) => kind == "clear" ? "CancelClear" : "CaptureCancelButton";

    private static T Find<T>(Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().Single(item => item.Name == name);

    private static void Settle(params Window[] windows)
    {
        Dispatcher.UIThread.RunJobs();
        foreach (var window in windows) window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void SendKey(Window window, Key key, PhysicalKey physicalKey, string? text)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, text);
        if (window.IsVisible) window.KeyRelease(key, RawInputModifiers.None, physicalKey, text);
        Dispatcher.UIThread.RunJobs();
    }

    private static double D(string token) => Convert.ToDouble(Avalonia.Application.Current!.Resources[token], System.Globalization.CultureInfo.InvariantCulture);

    private static Color SemanticColor(string theme, string token) => Color.Parse(DesignTokenCatalog.LoadDefault().Text($"semantic.{theme}.color.{token}"));

    private static void AssertBrushColor(IBrush? brush, Color expected, string context)
    {
        var actual = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.True(actual.Color == expected, $"{context}: expected {expected}, got {actual.Color}.");
    }
}
