namespace MonopolyBank.Services;

public enum ToastKind { Info, Success, Error }

public sealed record Toast(int Id, string Text, ToastKind Kind, string? ActionText, Func<Task>? Action);

/// <summary>Small card-like notifications at the bottom of the screen (optionally with an action such as Undo).</summary>
public sealed class Toaster
{
    private int _next;
    private readonly List<Toast> _items = [];
    public IReadOnlyList<Toast> Items => _items;
    public event Action? Changed;

    public void Show(string text, ToastKind kind = ToastKind.Info, string? actionText = null, Func<Task>? action = null)
    {
        var toast = new Toast(++_next, text, kind, actionText, action);
        // Keep it calm: at most 2 visible.
        while (_items.Count >= 2) _items.RemoveAt(0);
        _items.Add(toast);
        Changed?.Invoke();
        _ = DismissLater(toast, action is null ? 3000 : 5000);
    }

    public void Error(string text) => Show(text, ToastKind.Error);

    public void Dismiss(Toast toast)
    {
        if (_items.Remove(toast)) Changed?.Invoke();
    }

    private async Task DismissLater(Toast toast, int ms)
    {
        await Task.Delay(ms);
        Dismiss(toast);
    }
}
