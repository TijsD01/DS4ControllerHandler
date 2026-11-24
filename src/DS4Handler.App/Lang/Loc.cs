using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace DS4Handler.App.Lang;

/// <summary>
/// UI text in the chosen language (see <see cref="Apply"/>). Strings.resx holds Dutch, Strings.en.resx English;
/// a key missing in English falls back to Dutch. LocTests checks that every key used exists in both files.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Strings = new("DS4Handler.App.Lang.Strings", typeof(Loc).Assembly);

    /// <summary>Supported languages: settings value and the name shown in the menu (always in its own language).</summary>
    public static readonly (string Code, string Name)[] Languages = { ("nl", "Nederlands"), ("en", "English") };

    /// <summary>Culture for numbers and percentages in UI text; follows the UI language.</summary>
    public static CultureInfo Culture => CultureInfo.CurrentUICulture;

    private static readonly ResourceManager CodeStrings = new("DS4Handler.App.Lang.CodeStrings", typeof(Loc).Assembly); // TEMP: merged into Strings.resx

    public static string Get(string key) => Strings.GetString(key) ?? CodeStrings.GetString(key) ?? key;

    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    /// <summary>
    /// Sets the UI language for the whole process; call once at startup, before any window or text is made.
    /// <paramref name="code"/> null or unknown = the Windows language when it is Dutch, otherwise English.
    /// </summary>
    public static string Apply(string? code)
    {
        if (!Languages.Any(l => l.Code == code))
            code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl" ? "nl" : "en";
        var culture = CultureInfo.GetCultureInfo(code == "nl" ? "nl-NL" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return code!;
    }
}

/// <summary>XAML: <c>Text="{l:Loc Settings_Title}"</c>. Read once when the element loads.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
