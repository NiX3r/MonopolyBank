using Microsoft.JSInterop;

namespace MonopolyBank.Services;

/// <summary>Thin wrapper over wwwroot/js/app.js (window.mb).</summary>
public sealed class Browser(IJSRuntime js)
{
    public ValueTask<string?> GetItem(string key) => js.InvokeAsync<string?>("mb.storage.get", key);
    public ValueTask<bool> SetItem(string key, string value) => js.InvokeAsync<bool>("mb.storage.set", key, value);
    public ValueTask RemoveItem(string key) => js.InvokeVoidAsync("mb.storage.remove", key);
    public ValueTask<string[]> Languages() => js.InvokeAsync<string[]>("mb.languages");
    public ValueTask SetDocumentLanguage(string code, string oops, string reload) => js.InvokeVoidAsync("mb.setLang", code, oops, reload);
    public ValueTask CodeInput(Microsoft.AspNetCore.Components.ElementReference input, int maxChars) => js.InvokeVoidAsync("mb.codeInput", input, maxChars);
    public ValueTask Ping(string url) => js.InvokeVoidAsync("mb.ping", url);
    public ValueTask Vibrate(int ms = 15) => js.InvokeVoidAsync("mb.vibrate", ms);
    public ValueTask<bool> Copy(string text) => js.InvokeAsync<bool>("mb.copy", text);
    public ValueTask CoinDrag<T>(Microsoft.AspNetCore.Components.ElementReference root, DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("mb.coinDrag", root, target);
    public ValueTask KeepAwake(bool on) => js.InvokeVoidAsync("mb.keepAwake", on);
    public ValueTask<string> InstallState() => js.InvokeAsync<string>("mb.pwa.installState");
    public ValueTask<bool> Install() => js.InvokeAsync<bool>("mb.pwa.install");
    public ValueTask ApplyUpdate() => js.InvokeVoidAsync("mb.pwa.applyUpdate");
    public ValueTask OnUpdateReady<T>(DotNetObjectReference<T> target) where T : class =>
        js.InvokeVoidAsync("mb.pwa.onUpdateReady", target);
}
