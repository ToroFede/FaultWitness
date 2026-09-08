using System.Globalization;
using System.Resources;

namespace FaultWitness.Localization;

public sealed class LocalizationService
{
    private static readonly ResourceManager Resources = new("FaultWitness.Localization.Strings", typeof(LocalizationService).Assembly);
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public string Selection { get; private set; } = "system";
    public event EventHandler? Changed;
    public IReadOnlyList<CultureInfo> SupportedCultures { get; } = new[] { "en", "it", "es", "fr", "de", "pt", "ru", "pl" }.Select(CultureInfo.GetCultureInfo).ToList();
    public void SetCulture(string cultureName)
    {
        Selection = cultureName;
        var requested = cultureName == "system" ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : cultureName;
        Culture = SupportedCultures.FirstOrDefault(item => item.Name == requested) ?? CultureInfo.GetCultureInfo("en");
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public string Get(string key) => Resources.GetString(key, Culture) ?? Resources.GetString(key, CultureInfo.GetCultureInfo("en")) ?? key;
    public string Format(string key, params object[] values) => string.Format(Culture, Get(key), values);
}
