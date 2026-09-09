using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FaultWitness.Localization;
using FaultWitness.Rules;

namespace FaultWitness.UI.Tests;

[Trait("Suite", "Localization")]
public sealed class LocalizationTests
{
    [Fact]
    public void WhatChangedTitleExplicitlySaysObservedAndNear()
    {
        Assert.Equal("Changes detected near the first observed occurrence", Read("en")["ChangesNearFirst"]);
        Assert.Contains("available FaultWitness data", Read("en")["FirstObservedValue"], StringComparison.Ordinal);
        Assert.Contains("вблизи", Read("ru")["ChangesNearFirst"], StringComparison.Ordinal);
    }
    private static Dictionary<string, string> Read(string language)
    {
        var suffix = language == "en" ? "" : "." + language;
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "resources", "Strings" + suffix + ".resx"));
        return xml.Root!.Elements("data").ToDictionary(item => item.Attribute("name")!.Value, item => item.Element("value")!.Value);
    }
    // These short nouns also have identical spellings in some target languages (e.g. French "Source").
    private static readonly string[] IdenticalAllowed = ["AppTitle", "StrengthModerate", "General", "Incidents", "Date", "Format", "System", "Import", "SourceTypeInventory",
        "ChangeSubsystemAudio", "ChangeSubsystemSystem", "ChangeSource"];
    [Theory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void EveryLanguage_HasAllKeysNoEmptyStringsAndMatchingPlaceholders(string culture)
    {
        var english = Read("en"); var localized = Read(culture);
        Assert.Equal(english.Keys.Order(), localized.Keys.Order());
        foreach (var (key, value) in localized)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.Equal(Regex.Matches(english[key], @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order(),
                Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order());
            if (culture != "en" && !IdenticalAllowed.Contains(key, StringComparer.Ordinal)) Assert.NotEqual(english[key], value);
        }
    }
    [Theory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void AllDiagnosticRules_ResolveLocalizedConservativeContracts(string culture)
    {
        var service = new LocalizationService(); service.SetCulture(culture);
        foreach (var rule in RuleCatalog.Definitions)
        {
            Assert.NotEqual(rule.ObservedKey, service.Get(rule.ObservedKey));
            Assert.NotEqual(rule.InterpretationKey, service.Get(rule.InterpretationKey));
            Assert.NotEqual(rule.NotEstablishedKey, service.Get(rule.NotEstablishedKey));
            Assert.NotEqual(rule.RecommendedActionKey, service.Get(rule.RecommendedActionKey));
        }
        Assert.Contains("ntoskrnl", service.Get("rule.system.bugcheck.not_established"), StringComparison.Ordinal);
        Assert.Contains("PCIe", service.Get("rule.hardware.whea.pcie.observed"), StringComparison.Ordinal);
        Assert.Contains("APO", service.Get("rule.audio.repeated_apo_failure.observed"), StringComparison.Ordinal);
    }
    [Fact]
    public void UnknownLanguage_UsesEnglishFallbackWithoutChangingTechnicalKeys()
    {
        var service = new LocalizationService(); service.SetCulture("unsupported");
        Assert.Equal("Overview", service.Get("Overview")); Assert.Equal("nvlddmkm", service.Get("nvlddmkm"));
    }
    [Theory]
    [InlineData("en")][InlineData("it")][InlineData("es")][InlineData("fr")]
    [InlineData("de")][InlineData("pt")][InlineData("ru")][InlineData("pl")]
    public void ProcessMicrocopy_IsPresentAndSettingsSubtitleIsSpecific(string culture)
    {
        var localized = Read(culture);
        foreach (var key in new[] { "LocalAnalysisOnly", "SettingsPurpose", "SystemInformation", "NavigationCurrent", "DevelopmentProcessContext" })
            Assert.False(string.IsNullOrWhiteSpace(localized[key]), key);
        Assert.NotEqual(localized["Tagline"], localized["SettingsPurpose"]);
    }
    [Fact]
    public void SystemDefault_UsesSupportedCurrentUiCulture()
    {
        var before = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("it-IT"); var service = new LocalizationService(); service.SetCulture("system"); Assert.Equal("it", service.Culture.Name); }
        finally { CultureInfo.CurrentUICulture = before; }
    }
}
