using MonopolyBank.Core;

namespace MonopolyBank.Services;

/// <summary>Owns the persisted state. Every change goes through here so it is saved to localStorage immediately.</summary>
public sealed class GameStore(Browser browser)
{
    private const string StateKey = "mb.state.v1";
    private const string PrefsKey = "mb.prefs.keepAwake";

    public SavedState State { get; private set; } = new();
    public SharedGame? Shared => State.Mode == GameMode.Shared ? State.Shared : null;
    public WalletGame? Wallet => State.Mode == GameMode.Wallet ? State.Wallet : null;
    public GameSettings? Settings => Shared?.Settings ?? Wallet?.Settings;
    public bool KeepAwake { get; private set; } = true;

    public event Action? Changed;

    public async Task LoadAsync()
    {
        State = SavedState.FromJson(await browser.GetItem(StateKey));
        KeepAwake = await browser.GetItem(PrefsKey) != "0";
        await browser.KeepAwake(KeepAwake && State.HasGame);
    }

    /// <summary>Runs a game action, persists, notifies. BankException propagates to the caller (UI shows it).</summary>
    public async Task<T> Run<T>(Func<T> action)
    {
        var result = action();
        await SaveAsync();
        await browser.Vibrate();
        return result;
    }

    public async Task StartAsync(SharedGame game)
    {
        State = new SavedState { Mode = GameMode.Shared, Shared = game };
        await SaveAsync();
        await browser.KeepAwake(KeepAwake);
    }

    public async Task StartAsync(WalletGame game)
    {
        State = new SavedState { Mode = GameMode.Wallet, Wallet = game };
        await SaveAsync();
        await browser.KeepAwake(KeepAwake);
    }

    public async Task EndGameAsync()
    {
        State = new SavedState();
        await browser.RemoveItem(StateKey);
        await browser.KeepAwake(false);
        Changed?.Invoke();
    }

    public async Task SetKeepAwakeAsync(bool on)
    {
        KeepAwake = on;
        await browser.SetItem(PrefsKey, on ? "1" : "0");
        await browser.KeepAwake(on && State.HasGame);
        Changed?.Invoke();
    }

    public async Task SaveAsync()
    {
        await browser.SetItem(StateKey, State.ToJson());
        Changed?.Invoke();
    }
}
