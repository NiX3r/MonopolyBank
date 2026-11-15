using MonopolyBank.Core;

namespace MonopolyBank.Services;

/// <summary>Game editions: built-in presets plus the player's own, saved in localStorage (never leaves the device).</summary>
public sealed class EditionStore(Browser browser)
{
    private const string Key = "mb.editions";
    private List<Edition> _mine = [];

    public IReadOnlyList<Edition> Mine => _mine;
    public IEnumerable<Edition> All => Editions.BuiltIn.Concat(_mine);

    public async Task LoadAsync() => _mine = Editions.FromJson(await browser.GetItem(Key));

    public async Task<Edition> SaveAsync(GameSettings settings, string name)
    {
        var edition = Edition.From(settings, name);
        _mine.RemoveAll(e => string.Equals(e.Name, edition.Name, StringComparison.CurrentCultureIgnoreCase));
        _mine.Add(edition);
        await browser.SetItem(Key, Editions.ToJson(_mine));
        return edition;
    }

    public async Task DeleteAsync(Edition edition)
    {
        _mine.Remove(edition);
        await browser.SetItem(Key, Editions.ToJson(_mine));
    }

    /// <summary>Best guess for a typed game code: an edition with the same starting money and GO salary.</summary>
    public Edition Guess(long startMoney, long goSalary) =>
        _mine.Concat(Editions.BuiltIn).FirstOrDefault(e => e.StartMoney == startMoney && e.GoSalary == goSalary)
        ?? Editions.Default;
}
