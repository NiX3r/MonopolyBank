using System.Text.Json.Serialization;

namespace MonopolyBank.Core;

/// <summary>
/// Each phone is one player's wallet. No sync: bank operations are local (honor system, like real cash),
/// player-to-player payments travel as <see cref="TransferCode"/>s (QR or typed).
/// </summary>
public sealed class WalletGame
{
    public uint GameId { get; set; }
    public bool IsHost { get; set; }
    public GameSettings Settings { get; set; } = new();
    public Player Me { get; set; } = new();
    public List<Transaction> History { get; set; } = [];
    /// <summary>Transfer ids already sent or received by this wallet (replay protection).</summary>
    public HashSet<uint> SeenTransfers { get; set; } = [];
    public DateTimeOffset Started { get; set; }

    [JsonIgnore] public TimeProvider Clock { get; set; } = TimeProvider.System;
    [JsonIgnore] public Party MeParty => Party.Of(Me.Id);
    [JsonIgnore] public GameCode Code => new(GameId, Settings.StartMoney, Settings.GoSalary);

    public static WalletGame Host(GameSettings settings, string name, string token, string color, TimeProvider? clock = null)
    {
        settings.Validate();
        if (!GameCode.CanEncode(settings.StartMoney, settings.GoSalary)) throw new BankException(BankError.InvalidSettings);
        settings.FreeParkingPot = false; // a pot needs shared state
        return Create((uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 1 << 24), true, settings, name, token, color, clock);
    }

    /// <param name="look">Currency and its position: only change how money looks on this phone.</param>
    public static WalletGame Join(GameCode code, GameSettings look, string name, string token, string color, TimeProvider? clock = null)
    {
        var settings = look.Clone();
        settings.StartMoney = code.StartMoney;
        settings.GoSalary = code.GoSalary;
        settings.FreeParkingPot = false;
        settings.Validate();
        return Create(code.GameId, false, settings, name, token, color, clock);
    }

    private static WalletGame Create(uint gameId, bool host, GameSettings settings, string name, string token, string color, TimeProvider? clock)
    {
        var game = new WalletGame
        {
            GameId = gameId,
            IsHost = host,
            Settings = settings,
            Clock = clock ?? TimeProvider.System,
            Me = new Player { Name = Player.CleanName(name), Token = token, Color = color, Balance = settings.StartMoney },
        };
        game.Started = game.Clock.GetUtcNow();
        return game;
    }

    public Transaction CollectFromBank(long amount) => Add(TxKind.Payment, Party.Bank, MeParty, amount);

    public Transaction PassGo() => Add(TxKind.PassGo, Party.Bank, MeParty, Settings.GoSalary);

    public Transaction PayBank(long amount) => Add(TxKind.Payment, MeParty, Party.Bank, amount);

    /// <summary>Deducts the amount now and returns the code the receiver must scan/type.</summary>
    public (Transaction Tx, string Code) Send(long amount, string? toName = null)
    {
        CheckAmount(amount);
        if (Me.Balance < amount) throw new BankException(BankError.InsufficientFunds);
        if (!TransferCode.CanSend(amount)) throw new BankException(BankError.AmountTooPrecise);
        uint id;
        do id = TransferCode.NewId(); while (SeenTransfers.Contains(id));
        var code = new TransferCode(id, amount).Encode(GameId);
        SeenTransfers.Add(id);
        var tx = Add(TxKind.TransferOut, MeParty, new Party(PartyKind.Player), amount);
        tx.Code = code;
        tx.Counterparty = string.IsNullOrWhiteSpace(toName) ? null : toName.Trim();
        return (tx, code);
    }

    /// <summary>Refunds a sent transfer (only if the receiver did not scan it — honor system).</summary>
    public Transaction CancelSend(Guid transferTxId)
    {
        var sent = History.FirstOrDefault(t => t.Id == transferTxId && t.Kind == TxKind.TransferOut)
                   ?? throw new BankException(BankError.CannotUndo);
        if (sent.Cancelled) throw new BankException(BankError.CannotUndo);
        sent.Cancelled = true;
        var tx = Add(TxKind.TransferCancelled, new Party(PartyKind.Player), MeParty, sent.Amount);
        tx.Code = sent.Code;
        tx.Counterparty = sent.Counterparty;
        return tx;
    }

    /// <summary>Accepts a scanned QR payload or a typed code.</summary>
    public Transaction Receive(string input)
    {
        var (raw, sender) = QrPayload.SplitTransfer(input);
        if (!TransferCode.TryParse(raw, GameId, out var code)) throw new BankException(BankError.InvalidCode);
        if (SeenTransfers.Contains(code.TransferId))
        {
            var mine = History.Any(t => t.Kind == TxKind.TransferOut
                && TransferCode.TryParse(t.Code, GameId, out var sent) && sent.TransferId == code.TransferId);
            throw new BankException(mine ? BankError.OwnTransfer : BankError.AlreadyReceived);
        }
        SeenTransfers.Add(code.TransferId);
        var tx = Add(TxKind.TransferIn, new Party(PartyKind.Player), MeParty, code.Amount);
        tx.Code = code.Encode(GameId);
        tx.Counterparty = sender;
        return tx;
    }

    /// <summary>Undo works only for the latest bank operation; transfers use <see cref="CancelSend"/>.</summary>
    public Transaction Undo()
    {
        if (History.Count == 0) throw new BankException(BankError.NothingToUndo);
        var tx = History[^1];
        if (tx.Kind is not (TxKind.Payment or TxKind.PassGo)) throw new BankException(BankError.CannotUndo);
        Me.Balance += tx.From == MeParty ? tx.Amount : -tx.Amount;
        History.RemoveAt(History.Count - 1);
        return tx;
    }

    private Transaction Add(TxKind kind, Party from, Party to, long amount)
    {
        CheckAmount(amount);
        if (from == MeParty && Me.Balance < amount) throw new BankException(BankError.InsufficientFunds);
        var tx = new Transaction { Kind = kind, From = from, To = to, Amount = amount, Time = Clock.GetUtcNow() };
        Me.Balance += from == MeParty ? -amount : amount;
        History.Add(tx);
        return tx;
    }

    private static void CheckAmount(long amount)
    {
        if (amount is <= 0 or > Limits.MaxAmount) throw new BankException(BankError.InvalidAmount);
    }
}
