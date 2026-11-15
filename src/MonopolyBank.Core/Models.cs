namespace MonopolyBank.Core;

public enum GameMode { Shared, Wallet }

public enum PartyKind { Bank, Pot, Player }

/// <summary>Who money moves from/to. In wallet mode other players have no Id, only a name on the transaction.</summary>
public sealed record Party(PartyKind Kind, Guid? PlayerId = null)
{
    public static readonly Party Bank = new(PartyKind.Bank);
    public static readonly Party Pot = new(PartyKind.Pot);
    public static Party Of(Guid playerId) => new(PartyKind.Player, playerId);
}

public enum TxKind
{
    Payment,
    PassGo,
    Bankruptcy,
    TransferOut,
    TransferIn,
    TransferCancelled,
}

public sealed class GameSettings
{
    public long StartMoney { get; set; } = 1500;
    public long GoSalary { get; set; } = 200;
    public string Currency { get; set; } = "M";
    /// <summary>Symbol after the number ("1 500 Kč") instead of before ("M1,500").</summary>
    public bool CurrencyAfter { get; set; }
    /// <summary>House rule: taxes go to a Free Parking pot (shared mode only).</summary>
    public bool FreeParkingPot { get; set; }

    public void Validate()
    {
        if (StartMoney is < 0 or > Limits.MaxStartMoney) throw new BankException(BankError.InvalidSettings);
        if (GoSalary is < 0 or > Limits.MaxGoSalary) throw new BankException(BankError.InvalidSettings);
        Currency = (Currency ?? "").Trim();
        if (Currency.Length > Limits.MaxCurrencyLength) throw new BankException(BankError.InvalidSettings);
    }

    public GameSettings Clone() => new()
    {
        StartMoney = StartMoney, GoSalary = GoSalary, Currency = Currency, CurrencyAfter = CurrencyAfter,
        FreeParkingPot = FreeParkingPot,
    };
}

public static class Limits
{
    public const long MaxAmount = 999_999_999;
    public const long MaxStartMoney = MaxAmount;
    public const long MaxGoSalary = 99_999_999;
    public const int MaxCurrencyLength = 4;
    public const int MinPlayers = 2;
    public const int MaxPlayers = 8;
    public const int MaxNameLength = 20;
    public const int MaxEditionNameLength = 24;
}

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Token { get; set; } = "🎩";
    public string Color { get; set; } = "red";
    public long Balance { get; set; }
    public bool Bankrupt { get; set; }

    internal static string CleanName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new BankException(BankError.NameRequired);
        return n.Length > Limits.MaxNameLength ? n[..Limits.MaxNameLength] : n;
    }
}

public sealed class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; }
    public TxKind Kind { get; set; }
    public Party From { get; set; } = Party.Bank;
    public Party To { get; set; } = Party.Bank;
    public long Amount { get; set; }
    /// <summary>Wallet mode: name of the other player (if known).</summary>
    public string? Counterparty { get; set; }
    /// <summary>Wallet mode: transfer code (TransferOut / TransferIn).</summary>
    public string? Code { get; set; }
    /// <summary>Wallet mode: TransferOut that has been cancelled.</summary>
    public bool Cancelled { get; set; }
}

public enum BankError
{
    InvalidAmount,
    InsufficientFunds,
    SameParty,
    UnknownPlayer,
    PlayerBankrupt,
    PotDisabled,
    NothingToUndo,
    CannotUndo,
    InvalidCode,
    AlreadyReceived,
    OwnTransfer,
    NameRequired,
    TooFewPlayers,
    TooManyPlayers,
    InvalidSettings,
    AmountTooPrecise,
}

public sealed class BankException(BankError error) : Exception(error.ToString())
{
    public BankError Error { get; } = error;
}
