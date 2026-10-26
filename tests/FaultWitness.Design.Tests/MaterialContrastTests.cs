namespace FaultWitness.Design.Tests;

public sealed class MaterialContrastTests
{
    [Theory]
    [InlineData("light", "text")]
    [InlineData("light", "textMuted")]
    [InlineData("light", "accent")]
    [InlineData("dark", "text")]
    [InlineData("dark", "textMuted")]
    [InlineData("dark", "accent")]
    public void MaterialTextKeepsContrastEvenOverExtremeBackdropColors(string theme, string foreground)
    {
        var tokens = DesignTokenCatalog.LoadDefault();
        var ink = Parse(tokens.Text($"semantic.{theme}.color.{foreground}"));
        var canvas = Parse(tokens.Text($"semantic.{theme}.color.canvas"));
        var opacity = tokens.Number(theme == "light" ? "component.redesign.lightCanvasOpacity" : "component.redesign.canvasOpacity");
        foreach (var backdrop in new[] { 0d, 1d })
        {
            var composite = canvas.Select(channel => channel * opacity + backdrop * (1 - opacity)).ToArray();
            var a = Luminance(ink); var b = Luminance(composite);
            var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            Assert.True(ratio >= 4.5, $"{theme}/{foreground}: {ratio:0.00}");
        }
    }

    private static double[] Parse(string hex) => Enumerable.Range(0, 3).Select(i => Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255d).ToArray();
    private static double Luminance(double[] rgb)
    {
        var linear = rgb.Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
        return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
    }
}
