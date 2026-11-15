using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace MonopolyBank.Services;

/// <summary>
/// JSON-based localization. Every embedded i18n/xx.json is a language; "en" is the fallback.
/// Meta keys start with "_" (_name, _flag, _group = thousands separator).
/// </summary>
public sealed class Loc
{
    public const string Fallback = "en";
    private const string StorageKey = "mb.lang";

    public sealed record Language(string Code, string Name, string Flag, string Group);

    private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Browser _browser;
    private Dictionary<string, string> _current;

    public Loc(Browser browser)
    {
        _browser = browser;
        var asm = typeof(Loc).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("i18n/") && n.EndsWith(".json")))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            var dict = JsonSerializer.Deserialize(stream, I18nJson.Default.DictionaryStringString) ?? [];
            _texts[name[5..^5]] = dict;
        }
        Languages = _texts
            .Select(kv => new Language(kv.Key, Meta(kv.Value, "_name", kv.Key), Meta(kv.Value, "_flag", ""), Meta(kv.Value, "_group", ",")))
            .OrderBy(l => l.Code == Fallback ? 0 : 1).ThenBy(l => l.Name, StringComparer.Ordinal)
            .ToList();
        Current = Languages.First(l => l.Code == Fallback);
        _current = _texts[Fallback];
    }

    public IReadOnlyList<Language> Languages { get; }
    public Language Current { get; private set; }
    public event Action? Changed;

    public string this[string key] =>
        _current.TryGetValue(key, out var v) || _texts[Fallback].TryGetValue(key, out v) ? v : key;

    public string this[string key, params object?[] args] => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public async Task InitAsync()
    {
        var saved = await _browser.GetItem(StorageKey);
        var code = saved ?? Pick(await _browser.Languages());
        await SetAsync(code, persist: false);
    }

    public async Task SetAsync(string code, bool persist = true)
    {
        var lang = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                   ?? Languages.First(l => l.Code == Fallback);
        Current = lang;
        _current = _texts[lang.Code];
        if (persist) await _browser.SetItem(StorageKey, lang.Code);
        await _browser.SetDocumentLanguage(lang.Code, this["error.oops"], this["error.reload"]);
        Changed?.Invoke();
    }

    /// <summary>First browser language we have, matching "de-AT" to "de".</summary>
    public string Pick(IEnumerable<string> browserLanguages)
    {
        foreach (var bl in browserLanguages)
        {
            if (_texts.ContainsKey(bl)) return bl;
            var primary = bl.Split('-')[0];
            if (_texts.ContainsKey(primary)) return primary;
        }
        return Fallback;
    }

    public string Number(long n)
    {
        var s = Math.Abs(n).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", Current.Group);
        return n < 0 ? "−" + s : s;
    }

    private static string Meta(Dictionary<string, string> d, string key, string fallback) =>
        d.TryGetValue(key, out var v) ? v : fallback;
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class I18nJson : System.Text.Json.Serialization.JsonSerializerContext;
