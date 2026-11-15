using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MonopolyBank;
using MonopolyBank.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<Browser>();
builder.Services.AddSingleton<Loc>();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton<Toaster>();
builder.Services.AddSingleton<EditionStore>();

var host = builder.Build();

await host.Services.GetRequiredService<Loc>().InitAsync();
await host.Services.GetRequiredService<GameStore>().LoadAsync();
await host.Services.GetRequiredService<EditionStore>().LoadAsync();

// Usage ping: an empty POST, no payload, no ids, no game data. Disabled when Ping:Url is empty.
var pingUrl = builder.Configuration["Ping:Url"];
if (!string.IsNullOrWhiteSpace(pingUrl))
    await host.Services.GetRequiredService<Browser>().Ping(pingUrl);

await host.RunAsync();
