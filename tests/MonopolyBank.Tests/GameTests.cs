using MonopolyBank.Core;

namespace MonopolyBank.Tests;

public class SharedGameTests
{
    private static SharedGame NewGame(bool pot = false) => SharedGame.Create(
        new GameSettings { StartMoney = 1500, GoSalary = 200, FreeParkingPot = pot },
        [new("Anna", "🎩", "red"), new("Ben", "🚗", "blue"), new("Cyril", "🐕", "green")]);

    [Fact]
    public void Create_GivesStartMoney()
    {
        var g = NewGame();
        Assert.All(g.Players, p => Assert.Equal(1500, p.Balance));
    }

    [Fact]
    public void Create_RequiresTwoPlayers_AndNames()
    {
        Assert.Equal(BankError.TooFewPlayers,
            Assert.Throws<BankException>(() => SharedGame.Create(new(), [new("A", "", "")])).Error);
        Assert.Equal(BankError.NameRequired,
            Assert.Throws<BankException>(() => SharedGame.Create(new(), [new("A", "", ""), new("  ", "", "")])).Error);
    }

    [Fact]
    public void Pay_BetweenPlayers_AndUndo()
    {
        var g = NewGame();
        var (a, b) = (g.Players[0], g.Players[1]);
        g.Pay(Party.Of(a.Id), Party.Of(b.Id), 300);
        Assert.Equal(1200, a.Balance);
        Assert.Equal(1800, b.Balance);
        g.Undo();
        Assert.Equal(1500, a.Balance);
        Assert.Equal(1500, b.Balance);
        Assert.Empty(g.History);
    }

    [Fact]
    public void PassGo_PaysSalary()
    {
        var g = NewGame();
        g.PassGo(g.Players[0].Id);
        Assert.Equal(1700, g.Players[0].Balance);
        Assert.Equal(TxKind.PassGo, g.History[^1].Kind);
    }

    [Theory]
    [InlineData(0, BankError.InvalidAmount)]
    [InlineData(-5, BankError.InvalidAmount)]
    [InlineData(1501, BankError.InsufficientFunds)]
    public void Pay_Validates(long amount, BankError expected)
    {
        var g = NewGame();
        var ex = Assert.Throws<BankException>(() => g.Pay(Party.Of(g.Players[0].Id), Party.Bank, amount));
        Assert.Equal(expected, ex.Error);
        Assert.Equal(1500, g.Players[0].Balance);
    }

    [Fact]
    public void Pot_OnlyWhenEnabled()
    {
        var off = NewGame();
        Assert.Equal(BankError.PotDisabled,
            Assert.Throws<BankException>(() => off.Pay(Party.Of(off.Players[0].Id), Party.Pot, 50)).Error);

        var on = NewGame(pot: true);
        on.Pay(Party.Of(on.Players[0].Id), Party.Pot, 50);
        on.Pay(Party.Of(on.Players[1].Id), Party.Pot, 100);
        Assert.Equal(150, on.Pot);
        on.Pay(Party.Pot, Party.Of(on.Players[2].Id), on.Pot);
        Assert.Equal(0, on.Pot);
        Assert.Equal(1650, on.Players[2].Balance);
    }

    [Fact]
    public void Bankruptcy_MovesEverything_AndBlocksPlayer_UndoRestores()
    {
        var g = NewGame();
        var (a, b) = (g.Players[0], g.Players[1]);
        g.DeclareBankrupt(a.Id, Party.Of(b.Id));
        Assert.True(a.Bankrupt);
        Assert.Equal(0, a.Balance);
        Assert.Equal(3000, b.Balance);
        Assert.Equal(BankError.PlayerBankrupt,
            Assert.Throws<BankException>(() => g.PassGo(a.Id)).Error);
        g.Undo();
        Assert.False(a.Bankrupt);
        Assert.Equal(1500, a.Balance);
    }

    [Fact]
    public void SavedState_RoundTrips()
    {
        var g = NewGame(pot: true);
        g.Pay(Party.Of(g.Players[0].Id), Party.Pot, 75);
        var json = new SavedState { Mode = GameMode.Shared, Shared = g }.ToJson();
        var back = SavedState.FromJson(json);
        Assert.True(back.HasGame);
        Assert.Equal(75, back.Shared!.Pot);
        Assert.Equal(g.Players[0].Id, back.Shared.History[0].From.PlayerId);
        Assert.Equal(Party.Pot, back.Shared.History[0].To);
        back.Shared.Undo();
        Assert.Equal(1500, back.Shared.Players[0].Balance);
    }

    [Fact]
    public void SavedState_BadJson_GivesEmptyState()
    {
        Assert.False(SavedState.FromJson("{not json").HasGame);
        Assert.False(SavedState.FromJson(null).HasGame);
    }
}

public class WalletGameTests
{
    private static (WalletGame Host, WalletGame Guest) NewPair()
    {
        var host = WalletGame.Host(new GameSettings { StartMoney = 1500, GoSalary = 200 }, "Host", "🎩", "red");
        Assert.True(QrPayload.TryGetGameCode(QrPayload.ForGame(host.Code), out var code));
        var guest = WalletGame.Join(code, new GameSettings(), "Guest", "🚗", "blue");
        return (host, guest);
    }

    [Fact]
    public void Join_CopiesRules()
    {
        var (host, guest) = NewPair();
        Assert.Equal(host.GameId, guest.GameId);
        Assert.Equal(1500, guest.Me.Balance);
        Assert.Equal(200, guest.Settings.GoSalary);
    }

    [Fact]
    public void Transfer_MovesMoney_Once()
    {
        var (host, guest) = NewPair();
        var (_, code) = host.Send(250);
        Assert.Equal(1250, host.Me.Balance);

        var tx = guest.Receive(QrPayload.ForTransfer(code, host.Me.Name));
        Assert.Equal(1750, guest.Me.Balance);
        Assert.Equal("Host", tx.Counterparty);

        Assert.Equal(BankError.AlreadyReceived, Assert.Throws<BankException>(() => guest.Receive(code)).Error);
        Assert.Equal(BankError.OwnTransfer, Assert.Throws<BankException>(() => host.Receive(code)).Error);
    }

    [Fact]
    public void Transfer_FromOtherGame_IsRejected()
    {
        var (host, _) = NewPair();
        var (stranger, _) = NewPair();
        var (_, code) = stranger.Send(100);
        Assert.Equal(BankError.InvalidCode, Assert.Throws<BankException>(() => host.Receive(code)).Error);
    }

    [Fact]
    public void Send_RequiresFunds_AndCancelRefunds()
    {
        var (host, _) = NewPair();
        Assert.Equal(BankError.InsufficientFunds, Assert.Throws<BankException>(() => host.Send(1501)).Error);
        var (tx, _) = host.Send(500);
        host.CancelSend(tx.Id);
        Assert.Equal(1500, host.Me.Balance);
        Assert.Throws<BankException>(() => host.CancelSend(tx.Id));
    }

    [Fact]
    public void BankOps_AndUndo()
    {
        var (host, _) = NewPair();
        host.PassGo();
        host.PayBank(100);
        host.CollectFromBank(50);
        Assert.Equal(1650, host.Me.Balance);
        host.Undo();
        host.Undo();
        Assert.Equal(1700, host.Me.Balance);
        var (_, code) = host.Send(10);
        Assert.Equal(BankError.CannotUndo, Assert.Throws<BankException>(() => host.Undo()).Error);
    }

    [Fact]
    public void SavedState_RoundTrips_SeenTransfers()
    {
        var (host, guest) = NewPair();
        var (_, code) = host.Send(100);
        guest.Receive(code);
        var back = SavedState.FromJson(new SavedState { Mode = GameMode.Wallet, Wallet = guest }.ToJson()).Wallet!;
        Assert.Equal(BankError.AlreadyReceived, Assert.Throws<BankException>(() => back.Receive(code)).Error);
        Assert.Equal(1600, back.Me.Balance);
    }
}
