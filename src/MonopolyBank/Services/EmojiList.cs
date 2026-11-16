namespace MonopolyBank.Services;

/// <summary>
/// Every emoji for the token picker, grouped like the phone keyboards (embedded Data/emoji.txt, generated from
/// Unicode emoji-test.txt). Loaded on first use, so the setup pages don't pay for it.
/// </summary>
public static class EmojiList
{
    public sealed record Group(string Key, string Icon, string[] Emoji);

    // Tab icons; the keys match the "emoji.<key>" texts in i18n.
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["smileys"] = "😀", ["people"] = "👋", ["animals"] = "🐻", ["food"] = "🍔", ["travel"] = "🚗",
        ["activities"] = "⚽", ["objects"] = "💡", ["symbols"] = "❤️", ["flags"] = "🏁",
    };

    private static readonly Lazy<IReadOnlyList<Group>> Lazy = new(Load);

    public static IReadOnlyList<Group> Groups => Lazy.Value;

    private static IReadOnlyList<Group> Load()
    {
        using var stream = typeof(EmojiList).Assembly.GetManifestResourceStream("emoji.txt")!;
        using var reader = new StreamReader(stream);
        var groups = new List<Group>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            groups.Add(new Group(parts[0], Icons.GetValueOrDefault(parts[0], parts[1]), parts[1..]));
        }
        return groups;
    }
}
