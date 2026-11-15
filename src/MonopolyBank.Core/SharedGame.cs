using System.Text.Json.Serialization;

namespace MonopolyBank.Core;

/// <summary>One device holds the whole table: all players, the bank and the optional Free Parking pot.</summary>
public sealed class SharedGame
{
    public GameSettings Settings { get; set; } = new();
    public List<Player> Players { get; set; } = [];
    public long Pot { get; set; }
    public List<Transaction> History { get; set; } = [];
    public DateTimeOffset Started { get; set; }

    [JsonIgnore] public TimeProvider Clock { get; set; } = TimeProvider.System;

    public record NewPlayer(string Name, string Token, string Color);

    public static SharedGame Create(GameSettings settings, IReadOnlyList<NewPlayer> players, TimeProvider? clock = null)
    {
        settings.Validate();
        if (players.Count < Limits.MinPlayers) throw new BankException(BankError.TooFewPlayers);
        if (players.Count > Limits.MaxPlayers) throw new BankException(BankError.TooManyPlayers);
        var game = new SharedGame { Settings = settings, Clock = clock ?? TimeProvider.System };
        game.Started = game.Clock.GetUtcNow();
        foreach (var p in players)
        {
            game.Players.Add(new Player
            {
                Name = Player.CleanName(p.Name),
                Token = p.Token,
                Color = p.Color,
                Balance = settings.StartMoney,
            });
        }
        return game;
    }

    public Player GetPlayer(Guid id) =>
        Players.FirstOrDefault(p => p.Id == id) ?? throw new BankException(BankError.UnknownPlayer);

    public long BalanceOf(Party party) => party.Kind switch
    {
        PartyKind.Bank => long.MaxValue,
        PartyKind.Pot => Pot,
        _ => GetPlayer(party.PlayerId!.Value).Balance,
    };

    public Transaction Pay(Party from, Party to, long amount) => Move(from, to, amount, TxKind.Payment);

    public Transaction PassGo(Guid playerId) => Move(Party.Bank, Party.Of(playerId), Settings.GoSalary, TxKind.PassGo);

    /// <summary>Player hands everything left to the creditor (bank or another player) and leaves the game.</summary>
    public Transaction DeclareBankrupt(Guid playerId, Party creditor)
    {
        var player = GetPlayer(playerId);
        if (player.Bankrupt) throw new BankException(BankError.PlayerBankrupt);
        if (creditor.Kind == PartyKind.Pot) creditor = Party.Bank;
        if (creditor.Kind == PartyKind.Player)
        {
            if (creditor.PlayerId == playerId) throw new BankException(BankError.SameParty);
            if (GetPlayer(creditor.PlayerId!.Value).Bankrupt) throw new BankException(BankError.PlayerBankrupt);
        }
        var tx = NewTx(TxKind.Bankruptcy, Party.Of(playerId), creditor, player.Balance);
        Apply(tx, +1);
        player.Bankrupt = true;
        History.Add(tx);
        return tx;
    }

    /// <summary>Reverts the most recent transaction.</summary>
    public Transaction Undo()
    {
        if (History.Count == 0) throw new BankException(BankError.NothingToUndo);
        var tx = History[^1];
        if (tx.Kind == TxKind.Bankruptcy) GetPlayer(tx.From.PlayerId!.Value).Bankrupt = false;
        Apply(tx, -1);
        History.RemoveAt(History.Count - 1);
        return tx;
    }

    public IEnumerable<Player> ActivePlayers => Players.Where(p => !p.Bankrupt);

    private Transaction Move(Party from, Party to, long amount, TxKind kind)
    {
        if (amount is <= 0 or > Limits.MaxAmount) throw new BankException(BankError.InvalidAmount);
        if (from == to) throw new BankException(BankError.SameParty);
        foreach (var party in new[] { from, to })
        {
            if (party.Kind == PartyKind.Pot && !Settings.FreeParkingPot) throw new BankException(BankError.PotDisabled);
            if (party.Kind == PartyKind.Player && GetPlayer(party.PlayerId!.Value).Bankrupt)
                throw new BankException(BankError.PlayerBankrupt);
        }
        if (BalanceOf(from) < amount) throw new BankException(BankError.InsufficientFunds);
        var tx = NewTx(kind, from, to, amount);
        Apply(tx, +1);
        History.Add(tx);
        return tx;
    }

    private Transaction NewTx(TxKind kind, Party from, Party to, long amount) =>
        new() { Kind = kind, From = from, To = to, Amount = amount, Time = Clock.GetUtcNow() };

    private void Apply(Transaction tx, int sign)
    {
        Adjust(tx.From, -sign * tx.Amount);
        Adjust(tx.To, sign * tx.Amount);
    }

    private void Adjust(Party party, long delta)
    {
        switch (party.Kind)
        {
            case PartyKind.Pot: Pot += delta; break;
            case PartyKind.Player: GetPlayer(party.PlayerId!.Value).Balance += delta; break;
        }
    }
}
