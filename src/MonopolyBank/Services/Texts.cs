using MonopolyBank.Core;

namespace MonopolyBank.Services;

/// <summary>Turns game objects into localized, human-readable text.</summary>
public static class Texts
{
    public static readonly string[] Tokens = ["🎩", "🚗", "🐕", "🚢", "👢", "🐈", "🦆", "🐧", "🦖", "🚂", "🎸", "⚓"];
    public static readonly string[] Colors = ["brown", "lightblue", "pink", "orange", "red", "yellow", "green", "darkblue"];

    public static string Error(this Loc l, BankException ex) => l["err." + ex.Error];

    /// <summary>Plain-text money, e.g. "M1,500" or "1 500 Kč".</summary>
    public static string Money(this Loc l, GameSettings? s, long amount)
    {
        var cur = s?.Currency ?? "M";
        var num = l.Number(amount);
        return cur.Length == 0 ? num : s?.CurrencyAfter == true ? num + " " + cur : cur + num;
    }

    public static string EditionName(this Loc l, Edition e) => e.Name ?? l["edition." + e.Id];

    public static string PartyName(this Loc l, SharedGame g, Party p) => p.Kind switch
    {
        PartyKind.Bank => l["party.bank"],
        PartyKind.Pot => l["party.pot"],
        _ => g.Players.FirstOrDefault(x => x.Id == p.PlayerId)?.Name ?? "?",
    };

    public static string Describe(this Loc l, SharedGame g, Transaction tx) => tx.Kind switch
    {
        TxKind.PassGo => l["tx.passGo", l.PartyName(g, tx.To)],
        TxKind.Bankruptcy => l["tx.bankrupt", l.PartyName(g, tx.From), l.PartyName(g, tx.To)],
        _ => l["tx.payment", l.PartyName(g, tx.From), l.PartyName(g, tx.To)],
    };

    public static string Describe(this Loc l, WalletGame w, Transaction tx) => tx.Kind switch
    {
        TxKind.PassGo => l["wtx.passGo"],
        TxKind.Payment when tx.From.Kind == PartyKind.Bank => l["wtx.fromBank"],
        TxKind.Payment => l["wtx.toBank"],
        TxKind.TransferOut => tx.Counterparty is { } to ? l["wtx.sentTo", to] : l["wtx.sent"],
        TxKind.TransferIn => tx.Counterparty is { } from ? l["wtx.receivedFrom", from] : l["wtx.received"],
        TxKind.TransferCancelled => l["wtx.cancelled"],
        _ => tx.Kind.ToString(),
    };

    /// <summary>+1 money came in, −1 went out (from the point of view of the wallet owner).</summary>
    public static int Sign(this WalletGame w, Transaction tx) => tx.To == w.MeParty ? 1 : -1;
}
