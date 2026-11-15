# Monopoly Bank – private notes

## Build & test
```
dotnet test tests/MonopolyBank.Tests        # rules, codes, translations
dotnet test tests/MonopolyBank.E2E          # browser tests incl. offline + ping
```
First E2E run: `powershell -File tests/MonopolyBank.E2E/bin/Debug/net10.0/playwright.ps1 install chromium`.
Test a deployed site: `$env:MB_BASE_URL="https://bank.example/"; dotnet test tests/MonopolyBank.E2E`.

## Test on phones (LAN)
Camera needs HTTPS. Self-signed cert for the LAN IP lives in `artifacts/dev-cert/` (`dev.pfx`, password `monopoly-dev`; `MonopolyBank-dev.cer` to trust on the phone). Recreate it if the IP changes.
```
$env:ASPNETCORE_Kestrel__Certificates__Default__Path="artifacts/dev-cert/dev.pfx"
$env:ASPNETCORE_Kestrel__Certificates__Default__Password="monopoly-dev"
dotnet run --project src/MonopolyBank --launch-profile http --urls "https://0.0.0.0:5203"
```
Firewall (admin): `netsh advfirewall firewall add rule name="MonopolyBank dev" dir=in action=allow protocol=TCP localport=5203 remoteip=localsubnet profile=any`

## Deploy to IIS
1. Install the IIS **URL Rewrite** module (web.config needs it).
2. `dotnet publish src/MonopolyBank -c Release -o artifacts/publish`
3. Copy **contents of `artifacts/publish/wwwroot`** to the site root.
4. **HTTPS is required** (PWA install, service worker, camera).
5. Optional: enable IIS compression for `application/wasm` and `application/octet-stream` (server level). First load ≈ 7 MB uncompressed, ≈ 2 MB with compression. After that it is cached.

Sub-folder (e.g. `/bank/`): change `<base href>` in `wwwroot/index.html` (the service worker finds its folder itself).

Updates: publish again. Installed apps download the new version in the background and show “A new version is ready”.

## Deploy to GitHub Pages
`.github/workflows/pages.yml` builds, tests and deploys on every push to `main` (or Actions → Run workflow).
1. Push the repo to GitHub (Pages on a free account needs a **public** repo — `docs/private` and `CLAUDE.md` become readable too).
2. Settings → Pages → Source: **GitHub Actions**.
3. App URL: `https://<user>.github.io/<repo>/`. HTTPS is included, so install + camera work.
4. Ping: Pages has no server, so `api/ping` can't work there. Set Settings → Secrets and variables → Actions → **Variables** → `PING_URL` to an absolute URL (e.g. your IIS `https://server/api/ping`). No variable = ping off.

The workflow sets `<base href>` to `/<repo>/` and writes `appsettings.json` **before** publish (both are hashed into the offline cache list; editing them after publish breaks offline install). It adds `.nojekyll` (serve `_framework`) and `404.html` = copy of `index.html` (deep links like invite `join?g=…` load the app; first hit returns HTTP 404 but renders fine, later ones come from the service worker). Custom domain: base becomes `/` automatically.

## Usage ping
- `wwwroot/appsettings.json` → `Ping:Url` (default `api/ping`, relative to the site). Empty = off.
- On each app start, if online: one `POST` with **no body, no cookies**. Server still sees IP + user agent.
- `web.config` answers `POST /api/ping` with 204. Count usage from IIS logs:
  `Select-String -Path C:\inetpub\logs\LogFiles\W3SVC*\*.log -Pattern "POST /api/ping" | Measure-Object`
- Other endpoint (webhook, counter service): put the absolute URL in `Ping:Url`. Request is `no-cors`, so any server works.
- The privacy text in the app and public docs mentions the ping. Keep it true if you change it.

## Add a language
1. Copy `src/MonopolyBank/i18n/en.json` → `xx.json` (ISO 639-1 code, e.g. `fr`). Translate values, set `_name`, `_flag`, `_group` (thousands separator).
2. Copy `docs/public/en.md` → `docs/public/xx.md` and add it to `docs/public/README.md`.
3. `dotnet test tests/MonopolyBank.Tests` – fails if a key or `{0}` placeholder is missing.
No code change needed; the language appears automatically.

## How wallet mode works without a server
- Game code (16 chars): game id + start money + GO salary + CRC. Invite QR = `/join?g=CODE&c=CUR&ca=1` (c/ca = currency, symbol after; typed codes guess them from a matching edition).
- Transfer code (12 chars): 20-bit id + amount + CRC-16 keyed with the game id → codes from other games fail.
- Code v2 (app 1.1): amounts are 3-bit power of ten + mantissa (start 21 bits, GO 13, transfer 21), CRC XOR salt. v2 apps still read v1 codes; v1 apps reject v2 codes (all phones should update). Amounts > 2,097,151 must end in zeros (`err.AmountTooPrecise`).
- Payer deducts on creation; receiver credits once (remembers ids). Bank operations are honor system (each player enters their own).
- Limits: amount/start money ≤ 999,999,999; GO ≤ 99,999,999; 2–8 players (shared).

## Editions
Built-in presets: `Core/Editions.cs` (`Editions.BuiltIn`), names in i18n `edition.<id>`. Add one = entry there + key in every i18n file. Values vary between printings; players adjust before the game. No barcode lookup on purpose (no public catalogue; would break "no data leaves the device").

## Data
Everything is in `localStorage` (`mb.state.v1`, `mb.editions`, `mb.lang`, `mb.prefs.keepAwake`). Clearing site data = game lost.

## Third-party
jsQR (Apache-2.0, `wwwroot/lib/jsqr`), Archivo fonts (OFL, `wwwroot/fonts`), QRCoder (MIT, NuGet). “Monopoly” is a Hasbro trademark: fine for private use; rename before any public/commercial release.
