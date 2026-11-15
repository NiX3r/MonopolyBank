# CLAUDE.md

Offline PWA bank for the board game. Blazor WebAssembly standalone, .NET 10. No backend.

## Layout
- `src/MonopolyBank.Core` – all money rules, pure C#. `SharedGame` (one device), `WalletGame` (one player per phone), `Codes.cs` (game/transfer codes v2 with scaled amounts, still reads v1; QR payloads), `Editions.cs` (presets + user editions), `SavedState` (JSON, source-gen).
- `src/MonopolyBank` – UI. `Pages/` routes, `Components/` (all inherit `MbComponent` via `_Imports.razor`), `Services/` (`GameStore` = persistence, `Loc` = i18n, `Browser` = JS interop), `wwwroot/js/app.js` = `window.mb`.
- `tests/MonopolyBank.Tests` – xUnit (core + i18n completeness). `tests/MonopolyBank.E2E` – Playwright/NUnit, publishes Release and serves it with Kestrel.

## Rules
- Game logic goes in Core, never in Razor. Throw `BankException(BankError.X)`; UI maps to `err.X` text.
- Every state change: `Store.Run(() => game.Method())` (saves + vibrates).
- Text: `L["key"]` / `L["key", arg]`. Add key to `i18n/en.json` AND every other `i18n/*.json` (test enforces). Never hardcode UI text.
- Nothing leaves the device except the empty POST ping (`Ping:Url` in `wwwroot/appsettings.json`). Don't add network calls/CDNs; vendor files into `wwwroot/lib` or `wwwroot/fonts`.
- Wallet game/transfer code format is a wire format between phones. New version = new CRC salt (old apps reject instead of misreading); keep parsing old versions.
- Money display: `<Money>` / `L.Money(settings, n)` (respects `Currency`, `CurrencyAfter`). No banknotes: amounts are typed like a card payment (`MoneyPad`).
- Styling: single `wwwroot/css/app.css`, tokens in `:root`. No scoped CSS (styles.css link removed).
- `Sheet` with `Footer` needs explicit `<ChildContent>`.

## Commands
```
dotnet build
dotnet test tests/MonopolyBank.Tests
dotnet test tests/MonopolyBank.E2E          # first time: powershell -File tests/MonopolyBank.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet run --project src/MonopolyBank --launch-profile http   # http://localhost:5203
dotnet publish src/MonopolyBank -c Release -o artifacts/publish
```
Dev SW is a no-op; offline only works in published build. "New version ready" banner in dev after each rebuild is expected.

Hosting: IIS (`web.config`) or GitHub Pages (`.github/workflows/pages.yml` rewrites `<base href>` + `appsettings.json` before publish). Keep every app path relative (no leading `/`).

Docs: `docs/private/` (operator, EN), `docs/public/` (users, per language).
