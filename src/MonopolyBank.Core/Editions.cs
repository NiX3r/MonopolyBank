using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonopolyBank.Core;

/// <summary>
/// A version of the board game: currency, starting money and GO salary.
/// Built-in ones have an <see cref="Id"/> (name comes from i18n "edition.&lt;id&gt;"); the player's own have a <see cref="Name"/>.
/// </summary>
public sealed record Edition(string Id, string Currency, bool CurrencyAfter, long StartMoney, long GoSalary, string? Name = null)
{
    [JsonIgnore] public bool IsCustom => Name is not null;

    public void ApplyTo(GameSettings s)
    {
        s.Currency = Currency;
        s.CurrencyAfter = CurrencyAfter;
        s.StartMoney = StartMoney;
        s.GoSalary = GoSalary;
    }

    public bool Matches(GameSettings s) =>
        s.Currency == Currency && s.CurrencyAfter == CurrencyAfter && s.StartMoney == StartMoney &&
        s.GoSalary == GoSalary;

    public static Edition From(GameSettings s, string name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length == 0) throw new BankException(BankError.NameRequired);
        if (clean.Length > Limits.MaxEditionNameLength) clean = clean[..Limits.MaxEditionNameLength];
        var copy = s.Clone();
        copy.Validate();
        return new Edition("my-" + Guid.NewGuid().ToString("N")[..8], copy.Currency, copy.CurrencyAfter,
            copy.StartMoney, copy.GoSalary, clean);
    }
}

public static class Editions
{
    /// <summary>Common versions. Values differ between printings — players can adjust everything before the game.</summary>
    public static readonly IReadOnlyList<Edition> BuiltIn =
    [
        new("classic", "M", false, 1500, 200),
        new("dollar", "$", false, 1500, 200),
        new("pound", "£", false, 1500, 200),
        new("euro", "€", true, 1500, 200),
        new("millions", "M", false, 15_000_000, 2_000_000),
    ];

    public static Edition Default => BuiltIn[0];

    public static string ToJson(IEnumerable<Edition> editions) =>
        JsonSerializer.Serialize(editions.ToList(), EditionJson.Default.ListEdition);

    /// <summary>Lenient: broken or invalid entries are dropped.</summary>
    public static List<Edition> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize(json, EditionJson.Default.ListEdition) ?? [])
                .Where(e => e is { Name.Length: > 0, Currency: not null })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

[JsonSerializable(typeof(List<Edition>))]
internal partial class EditionJson : JsonSerializerContext;
