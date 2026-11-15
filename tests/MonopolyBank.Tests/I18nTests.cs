using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonopolyBank.Tests;

/// <summary>Every language file must have exactly the keys of en.json, with the same {n} placeholders.</summary>
public partial class I18nTests
{
    private static readonly string Dir = FindI18nDir();

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Dir, "*.json").Where(f => !f.EndsWith("en.json"))) data.Add(Path.GetFileName(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Language_IsComplete(string file)
    {
        var en = Load("en.json");
        var other = Load(file);
        Assert.Empty(en.Keys.Except(other.Keys).Order());   // missing translations
        Assert.Empty(other.Keys.Except(en.Keys).Order());   // stale keys
        foreach (var (key, text) in en)
            Assert.True(Placeholders(text).SetEquals(Placeholders(other[key])), $"{file}: placeholders differ for '{key}'");
        Assert.False(string.IsNullOrWhiteSpace(other["_name"]));
    }

    [Fact]
    public void AllPageKeys_ExistInEnglish()
    {
        var en = Load("en.json");
        var src = Path.GetFullPath(Path.Combine(Dir, ".."));
        var used = Directory.GetFiles(src, "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => KeyUse().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(k => !k.EndsWith('.'))
            .Distinct();
        var missing = used.Where(k => !en.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing in en.json: " + string.Join(", ", missing));
    }

    private static HashSet<string> Placeholders(string s) => [.. Regex.Matches(s, @"\{\d+\}").Select(m => m.Value)];

    private static Dictionary<string, string> Load(string file) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Dir, file)))!;

    [GeneratedRegex("""L\["([a-zA-Z.]+)["]""")]
    private static partial Regex KeyUse();

    private static string FindI18nDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MonopolyBank.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "MonopolyBank", "i18n");
    }
}
