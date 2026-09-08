using System.Text.Json;
using System.Text.RegularExpressions;
using FaultWitness.Design;

namespace FaultWitness.Design.Tests;

public sealed class DesignContractTests
{
    private static readonly string Root = FindRoot();

    [Fact] public void CanonicalTokens_AllResolveWithoutCyclesOrMissingReferences() => Assert.Empty(DesignTokenCatalog.LoadDefault().Validate());

    [Fact]
    public void LightAndDark_SemanticColorSetsAreCompleteAndEquivalent()
    {
        var catalog = DesignTokenCatalog.LoadDefault();
        var light = catalog.Keys.Where(x => x.StartsWith("semantic.light.color.", StringComparison.Ordinal)).Select(x => x[21..]).Order().ToArray();
        var dark = catalog.Keys.Where(x => x.StartsWith("semantic.dark.color.", StringComparison.Ordinal)).Select(x => x[20..]).Order().ToArray();
        Assert.Equal(light, dark);
    }

    [Theory]
    [InlineData("light", "text", "canvas", 4.5)]
    [InlineData("light", "textMuted", "canvas", 4.5)]
    [InlineData("dark", "text", "canvas", 4.5)]
    [InlineData("dark", "textMuted", "canvas", 4.5)]
    [InlineData("light", "attention", "canvas", 3.0)]
    [InlineData("dark", "attention", "canvas", 3.0)]
    public void SemanticColors_MeetDeclaredContrast(string theme, string foreground, string background, double minimum)
    {
        var tokens = DesignTokenCatalog.LoadDefault();
        Assert.True(Contrast(tokens.Text($"semantic.{theme}.color.{foreground}"), tokens.Text($"semantic.{theme}.color.{background}")) >= minimum);
    }

    [Fact]
    public void UiContract_ContainsEveryRequiredComponentAndField()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "design", "ui-contract.json")));
        var components = json.RootElement.GetProperty("components").EnumerateArray().ToArray();
        string[] required = ["PrimaryAction","SecondaryAction","SubtleAction","DangerAction","NavigationItem","NavigationGroup","SectionHeading","StatusBadge","IncidentRow","SummaryMetric","Callout","Disclosure","CommandArea","EmptyState","InlineStatus","Dialog"];
        Assert.Equal(required.Order(), components.Select(x => x.GetProperty("id").GetString()!).Order());
        string[] fields = ["role","allowedUse","forbiddenUse","visualPriority","typographyToken","surfaceToken","foregroundToken","borderToken","minSize","states","accessibilityRole","keyboardBehavior","iconPolicy","textPolicy"];
        Assert.All(components, component => Assert.All(fields, field => Assert.True(component.TryGetProperty(field, out _), $"{component.GetProperty("id")}: {field}")));
    }

    [Fact]
    public void ScreenSpecs_CoverArchitectureAndKeyboardResponsiveContracts()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "design", "screen-specs.json")));
        var screens = json.RootElement.GetProperty("screens").EnumerateArray().ToArray();
        string[] ids = ["Home","Analyze","Incidents","IncidentDetail","History","System","DiagnosticReadiness","Settings","ExportSupport"];
        Assert.Equal(ids.Order(), screens.Select(x => x.GetProperty("id").GetString()!).Order());
        Assert.All(screens, screen => { Assert.NotEmpty(screen.GetProperty("defaultFocus").GetString()!); Assert.NotEmpty(screen.GetProperty("keyboardFlow").EnumerateArray()); Assert.True(screen.GetProperty("responsiveRules").TryGetProperty("small", out _)); });
    }

    [Fact]
    public void AppStyling_HasNoRawColorLiteralsOutsideCanonicalTokens()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src", "FaultWitness.App"), "*.cs", SearchOption.AllDirectories)
            .Where(path => Regex.IsMatch(File.ReadAllText(path), "#[0-9A-Fa-f]{6,8}")).Select(Path.GetFileName).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void AcceptanceContract_SeparatesAutomaticSemiAutomaticAndManualVisualChecks()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "docs", "design", "ux-acceptance.json")));
        Assert.True(json.RootElement.GetProperty("automatic").GetArrayLength() >= 10);
        Assert.True(json.RootElement.GetProperty("semiAutomatic").GetArrayLength() >= 4);
        Assert.True(json.RootElement.GetProperty("manualVisual").GetArrayLength() >= 8);
    }
    [Fact]
    public void InteractionTargets_MeetDesktopAndAbsoluteMinimums()
    {
        var tokens = DesignTokenCatalog.LoadDefault();
        Assert.True(tokens.Number("component.action.standard.minHeight") >= 32);
        Assert.True(tokens.Number("component.action.primary.minHeight") >= 40);
        Assert.True(tokens.Number("component.navigation.itemHeight") >= 32);
        Assert.True(tokens.Number("component.row.minHeight") >= 32);
    }
    [Fact]
    public void AppTypography_UsesSemanticRolesInsteadOfNumericLabelSizes()
    {
        var sources = Directory.EnumerateFiles(Path.Combine(Root, "src", "FaultWitness.App"), "*.cs").Select(File.ReadAllText);
        Assert.DoesNotContain(sources, source => source.Split('\n').Any(line => Regex.IsMatch(line, "Label\\(.*?,\\s*(12|14|15|16|17|18|20|23|28|29|32)(?:,|\\))")));
    }
    [Fact]
    public void Localization_DoesNotExposeAHealthScoreConcept()
    {
        var resources = Directory.EnumerateFiles(Path.Combine(Root, "src", "FaultWitness.Localization"), "Strings*.resx").SelectMany(File.ReadLines).ToArray();
        Assert.DoesNotContain(resources, line => line.Contains("name=\"HealthScore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resources, line => Regex.IsMatch(line, @"<value>[^<]*\b\d+\s*/\s*100\b", RegexOptions.IgnoreCase));
    }

    private static double Contrast(string a, string b) { var x = Luminance(a); var y = Luminance(b); return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05); }
    private static double Luminance(string hex) { var v = Enumerable.Range(0,3).Select(i => Convert.ToInt32(hex.Substring(1+i*2,2),16)/255d).Select(x => x <= .04045 ? x/12.92 : Math.Pow((x+.055)/1.055,2.4)).ToArray(); return .2126*v[0]+.7152*v[1]+.0722*v[2]; }
    private static string FindRoot() { var path = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(path,"FaultWitness.slnx"))) path = Directory.GetParent(path)?.FullName ?? throw new DirectoryNotFoundException(); return path; }
}
