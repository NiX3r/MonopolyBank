# Monopoly Bank

Offline banking app for board game nights: no more paper money. Blazor WebAssembly PWA (.NET 10), installable, works without internet, no backend.

- **One shared bank** – one device holds every player's balance.
- **Wallet per player** – each phone is a wallet; pay each other via QR codes (or typed codes), no server needed.
- Languages: English, Čeština, Deutsch, Español (add one by dropping a JSON file).

```
dotnet run --project src/MonopolyBank --launch-profile http   # http://localhost:5203
dotnet test
```

Docs: [User guide](docs/public/README.md) · [Private / operations](docs/private/README.md) · [AI notes](CLAUDE.md)
