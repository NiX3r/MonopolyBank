using Microsoft.AspNetCore.Components;
using MonopolyBank.Core;
using MonopolyBank.Services;

namespace MonopolyBank.Components;

/// <summary>Base for every component: localization, game store, toasts; re-renders on language/state change.</summary>
public abstract class MbComponent : ComponentBase, IDisposable
{
    [Inject] protected Loc L { get; set; } = default!;
    [Inject] protected GameStore Store { get; set; } = default!;
    [Inject] protected Toaster Toasts { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    private bool _subscribed;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            L.Changed += Refresh;
            Store.Changed += Refresh;
        }
        return base.SetParametersAsync(parameters);
    }

    private void Refresh() => InvokeAsync(StateHasChanged);

    protected string Currency => Store.Settings?.Currency ?? "M";

    /// <summary>Plain-text money for toasts/labels, e.g. "M1,500" or "1 500 Kč".</summary>
    protected string Money(long amount) => L.Money(Store.Settings, amount);

    /// <summary>Runs an action, showing a localized toast for game-rule errors.</summary>
    protected async Task<bool> Try(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (BankException ex)
        {
            Toasts.Error(L.Error(ex));
            return false;
        }
    }

    public virtual void Dispose()
    {
        L.Changed -= Refresh;
        Store.Changed -= Refresh;
        GC.SuppressFinalize(this);
    }
}
