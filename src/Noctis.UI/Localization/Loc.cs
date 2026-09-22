using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Noctis.Localization;

/// <summary>
/// UI string lookup with live language switching. Strings live in <c>Localization/Strings.resx</c>
/// (English, the fallback) and one <c>Strings.{culture}.resx</c> per translation; the SDK
/// compiles them into satellite assemblies, so adding a language is adding a file — no code.
///
/// XAML binds through <see cref="TExtension"/> (<c>{loc:T Key}</c>) to the indexer here;
/// changing the culture raises one PropertyChanged for the indexer and every bound string
/// re-reads. Code uses <see cref="T"/>. Missing keys return the key itself so a typo is
/// visible in the UI instead of an empty label.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>Setting value meaning "follow the OS language".</summary>
    public const string SystemLanguage = "";

    /// <summary>
    /// Cultures that ship a translation, English first — discovered from the satellite
    /// assemblies next to the app (<c>&lt;culture&gt;/Noctis.resources.dll</c>), so a translation
    /// merged from Crowdin (https://crowdin.com/project/noctis) appears in the Language picker
    /// with no code change.
    /// </summary>
    public static IReadOnlyList<string> Supported => _supported ??= DiscoverSupported();
    private static IReadOnlyList<string>? _supported;

    /// <summary>
    /// Cultures compiled in as a fallback for hosts with no satellite directory to scan
    /// (Android embeds the satellite assemblies in the APK). Kept in sync with the
    /// Strings.*.resx files by LocalizationTests.KnownCultures_ListsEveryShippedTranslation.
    /// </summary>
    public static IReadOnlyList<string> KnownCultures { get; } =
        new[] { "ar", "es", "fr", "ja", "ko", "tr", "zh-Hans", "zh-Hant" };

    private static IReadOnlyList<string> DiscoverSupported()
    {
        var found = new List<string> { "en" };
        var assembly = typeof(Loc).Assembly;
        var satelliteName = assembly.GetName().Name + ".resources.dll";
        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(baseDir))
                {
                    if (!File.Exists(Path.Combine(dir, satelliteName))) continue;
                    var name = Path.GetFileName(dir);
                    try { _ = CultureInfo.GetCultureInfo(name); } catch (CultureNotFoundException) { continue; }
                    if (!found.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(name);
                }
            }
        }
        catch (Exception) { /* unreadable install dir: fall through to the probe */ }

        if (found.Count == 1)
        {
            // No satellite folders on disk (Android): ask the runtime for each known
            // culture's satellite assembly instead. GetSatelliteAssembly throws when the
            // culture did not ship, so the list stays honest about what is loadable.
            foreach (var name in KnownCultures)
            {
                try
                {
                    assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo(name));
                    found.Add(name);
                }
                catch (Exception) { /* not shipped in this build */ }
            }
        }

        return found.Skip(1).OrderBy(c => CultureInfo.GetCultureInfo(c).NativeName, StringComparer.CurrentCultureIgnoreCase).Prepend("en").ToList();
    }

    private readonly ResourceManager _resources = new("Noctis.Localization.Strings", typeof(Loc).Assembly);
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the culture changes, for code that caches strings (sidebar labels).</summary>
    public event EventHandler? CultureChanged;

    /// <summary>The culture strings are currently served in.</summary>
    public CultureInfo Culture => _culture;

    /// <summary>Indexer for XAML bindings: <c>Instance["Nav.Home"]</c>.</summary>
    public string this[string key] => Get(key);

    /// <summary>The string for <paramref name="key"/> in the current culture (English fallback, key on miss).</summary>
    public static string T(string key) => Instance.Get(key);

    /// <summary><see cref="T"/> with <see cref="string.Format(IFormatProvider, string, object[])"/> in the current culture.</summary>
    public static string T(string key, params object[] args)
        => string.Format(Instance._culture, Instance.Get(key), args);

    private string Get(string key)
    {
        try { return _resources.GetString(key, _culture) ?? key; }
        catch (MissingManifestResourceException) { return key; }
    }

    /// <summary>
    /// Switches the UI language. <paramref name="languageCode"/> is a culture name ("es",
    /// "pt-BR") or <see cref="SystemLanguage"/> to follow the OS. Unknown names fall back to
    /// the OS culture. Every <c>{loc:T}</c> binding re-reads; <see cref="CultureChanged"/> fires.
    /// </summary>
    public void SetCulture(string? languageCode)
    {
        CultureInfo culture;
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            culture = CultureInfo.InstalledUICulture;
        }
        else
        {
            try { culture = CultureInfo.GetCultureInfo(languageCode.Trim()); }
            catch (CultureNotFoundException) { culture = CultureInfo.InstalledUICulture; }
        }

        if (culture.Name == _culture.Name) return;
        _culture = culture;
        // "Item" is the CLR name of the indexer; Avalonia's indexer node re-reads only on that
        // (or an empty name), not on the WPF-style "Item[]".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The language a stored setting resolves to: the setting itself when it names a shipped
    /// translation, else the OS language's parent if that ships, else English. Used by the
    /// Settings picker to show which entry is active.
    /// </summary>
    public static string Resolve(string? setting)
    {
        var name = string.IsNullOrWhiteSpace(setting) ? CultureInfo.InstalledUICulture.Name : setting.Trim();
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(name); }
        catch (CultureNotFoundException) { return Supported[0]; }
        // Walk the parent chain the way ResourceManager does: zh-CN → zh-Hans → zh, es-MX → es.
        for (var c = culture; !c.Equals(CultureInfo.InvariantCulture); c = c.Parent)
        {
            var match = Supported.FirstOrDefault(s => c.Name.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return Supported[0];
    }
}
