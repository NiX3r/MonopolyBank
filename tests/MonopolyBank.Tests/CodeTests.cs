using MonopolyBank.Core;

namespace MonopolyBank.Tests;

public class CodeTests
{
    [Fact]
    public void Base32_RoundTrips()
    {
        byte[] data = [0, 1, 2, 250, 255, 128, 7, 9, 33, 64];
        var text = Base32.Encode(data);
        Assert.True(Base32.TryDecode(text, data.Length, out var back));
        Assert.Equal(data, back);
    }

    [Fact]
    public void Base32_IsLenient()
    {
        var text = Base32.Encode([0x12, 0x34, 0x56, 0x78, 0x9A]);
        var sloppy = string.Join(' ', text.ToLowerInvariant().Replace('0', 'o').Replace('1', 'l').Chunk(3).Select(c => new string(c)));
        Assert.True(Base32.TryDecode(sloppy, 5, out var back));
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A }, back);
    }

    [Fact]
    public void GameCode_RoundTrips_AndIsFormatted()
    {
        var code = new GameCode(0xABCDE, 1500, 200);
        var text = code.Encode();
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", text);
        Assert.True(GameCode.TryParse(text, out var back));
        Assert.Equal(code, back);
        Assert.True(QrPayload.TryGetGameCode(QrPayload.ForGame(code), out var fromQr));
        Assert.Equal(code, fromQr);
        Assert.True(QrPayload.TryGetGameCode(QrPayload.JoinUrl("https://bank.example/app/", code), out var fromUrl));
        Assert.Equal(code, fromUrl);
    }

    [Fact]
    public void GameCode_RejectsTypos()
    {
        var text = new GameCode(42, 1500, 200).Encode();
        var typo = (text[0] == 'A' ? 'B' : 'A') + text[1..];
        Assert.False(GameCode.TryParse(typo, out _));
        Assert.False(GameCode.TryParse("", out _));
        Assert.False(GameCode.TryParse("HELLO", out _));
    }

    [Fact]
    public void TransferCode_RoundTrips_WithinSameGame()
    {
        var code = new TransferCode(12345, 2_097_151);
        var text = code.Encode(777);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", text);
        Assert.True(TransferCode.TryParse(text, 777, out var back));
        Assert.Equal(code, back);
    }

    [Fact]
    public void TransferCode_FromOtherGame_IsRejected()
    {
        var text = new TransferCode(1, 100).Encode(777);
        Assert.False(TransferCode.TryParse(text, 778, out _));
    }

    [Fact]
    public void QrPayload_Transfer_CarriesSenderName()
    {
        var payload = QrPayload.ForTransfer("ABCD-EFGH-JKMN", "Žofie & Co:1");
        var (code, sender) = QrPayload.SplitTransfer(payload);
        Assert.Equal("ABCD-EFGH-JKMN", code);
        Assert.Equal("Žofie & Co:1", sender);
        Assert.Equal(("abcd", (string?)null), QrPayload.SplitTransfer(" abcd "));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(1500L)]
    [InlineData(2_097_151L)]
    [InlineData(15_000_000L)]
    [InlineData(20_000_000L)]
    [InlineData(999_000_000L)]
    public void ScaledAmount_RoundTrips(long value)
    {
        Assert.True(ScaledAmount.TryEncode(value, 21, out var bits));
        Assert.True(bits < 1u << 24);
        Assert.Equal(value, ScaledAmount.Decode(bits, 21));
    }

    [Fact]
    public void ScaledAmount_RejectsTooPrecise()
    {
        Assert.False(ScaledAmount.TryEncode(2_097_153, 21, out _));
        Assert.False(ScaledAmount.TryEncode(-1, 21, out _));
        Assert.False(TransferCode.CanSend(9_999_999));
        Assert.True(TransferCode.CanSend(10_000_000));
    }

    [Fact]
    public void BigEditions_FitInCodes()
    {
        foreach (var e in Editions.BuiltIn)
        {
            Assert.True(GameCode.CanEncode(e.StartMoney, e.GoSalary), e.Id);
            Assert.True(GameCode.TryParse(new GameCode(99, e.StartMoney, e.GoSalary).Encode(), out var back));
            Assert.Equal((e.StartMoney, e.GoSalary), (back.StartMoney, back.GoSalary));
            Assert.True(TransferCode.CanSend(e.StartMoney));
        }
        var big = new TransferCode(7, 20_000_000);
        Assert.True(TransferCode.TryParse(big.Encode(5), 5, out var t));
        Assert.Equal(big, t);
    }

    [Fact]
    public void Version1Codes_AreStillAccepted()
    {
        // Codes made by app 1.0 (raw amounts, plain CRC) must keep working in a running game.
        Span<byte> b = stackalloc byte[10];
        b[0] = 0; b[1] = 0; b[2] = 42;                 // game id 42
        b[3] = 0; b[4] = 0x05; b[5] = 0xDC;            // 1500
        b[6] = 0; b[7] = 200;
        var crc = Crc16(b[..8]);
        b[8] = (byte)(crc >> 8); b[9] = (byte)crc;
        Assert.True(GameCode.TryParse(Base32.Encode(b), out var game));
        Assert.Equal(new GameCode(42, 1500, 200), game);

        ulong payload = 1234UL << 24 | 3_000_000UL;     // v1 allowed raw amounts up to 9,999,999
        Span<byte> t = stackalloc byte[9];
        t[0] = 0; t[1] = 0; t[2] = 42;
        for (var i = 0; i < 6; i++) t[3 + i] = (byte)(payload >> (40 - 8 * i));
        var v1 = Base32.EncodeBits(payload << 16 | Crc16(t), 12);
        Assert.True(TransferCode.TryParse(v1, 42, out var transfer));
        Assert.Equal(new TransferCode(1234, 3_000_000), transfer);
    }

    [Fact]
    public void Version2Codes_LookInvalidToVersion1Check()
    {
        // The salt guarantees old apps reject new codes instead of misreading the amount.
        var text = new GameCode(42, 15_000_000, 2_000_000).Encode();
        Assert.True(Base32.TryDecode(text, 10, out var b));
        Assert.NotEqual(Crc16(b.AsSpan(0, 8)), (ushort)(b[8] << 8 | b[9]));
    }

    [Fact]
    public void JoinUrl_CarriesCurrency()
    {
        var look = new GameSettings { Currency = "Kč", CurrencyAfter = true };
        var url = QrPayload.JoinUrl("https://bank.example/", new GameCode(9, 15_000_000, 2_000_000), look);
        Assert.True(QrPayload.TryGetInvite(url, out var code, out var back));
        Assert.Equal(15_000_000, code.StartMoney);
        Assert.NotNull(back);
        Assert.Equal("Kč", back.Currency);
        Assert.True(back.CurrencyAfter);
        Assert.DoesNotContain("&b=", url);
        // Links from app 1.1 previews may still carry banknotes (b=…); they are ignored.
        Assert.True(QrPayload.TryGetInvite(url + "&b=1.5.10", out _, out var old));
        Assert.Equal("Kč", old!.Currency);
        Assert.True(QrPayload.TryGetInvite(new GameCode(9, 1500, 200).Encode(), out _, out var none));
        Assert.Null(none);
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var x in data)
        {
            crc ^= (ushort)(x << 8);
            for (var i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}

public class EditionTests
{
    [Fact]
    public void Edition_AppliesAndMatches()
    {
        var s = new GameSettings();
        var millions = Editions.BuiltIn.Single(e => e.Id == "millions");
        millions.ApplyTo(s);
        Assert.True(millions.Matches(s));
        Assert.Equal(15_000_000, s.StartMoney);
        s.StartMoney = 7;
        Assert.False(millions.Matches(s)); // edits don't touch the preset
        Assert.Equal(15_000_000, millions.StartMoney);
    }

    [Fact]
    public void CustomEditions_RoundTripJson()
    {
        var s = new GameSettings { Currency = "Kč", CurrencyAfter = true, StartMoney = 30_000, GoSalary = 4_000 };
        var mine = Edition.From(s, "  Babiččina krabice  ");
        Assert.Equal("Babiččina krabice", mine.Name);
        var back = Editions.FromJson(Editions.ToJson([mine]));
        Assert.Single(back);
        Assert.Equal(mine.Name, back[0].Name);
        Assert.True(back[0].Matches(new GameSettings { Currency = "Kč", CurrencyAfter = true, StartMoney = 30_000, GoSalary = 4_000 }));
        Assert.Empty(Editions.FromJson("{broken"));
        Assert.Throws<BankException>(() => Edition.From(s, " "));
    }

    [Fact]
    public void OlderData_WithBanknotes_StillLoads()
    {
        // Saved games and editions may contain "Bills" from an earlier build; it is ignored.
        var json = """{"Version":1,"Mode":"Wallet","Wallet":{"GameId":5,"Settings":{"StartMoney":1500,"GoSalary":200,"Currency":"M","Bills":[1,5]},"Me":{"Name":"A","Balance":1500}}}""";
        var state = SavedState.FromJson(json);
        Assert.True(state.HasGame);
        Assert.Equal(1500, state.Wallet!.Settings.StartMoney);
        Assert.False(state.Wallet.Settings.CurrencyAfter);

        var editions = Editions.FromJson("""[{"Id":"my-1","Currency":"Kč","CurrencyAfter":true,"StartMoney":30000,"GoSalary":4000,"Bills":[100,500],"Name":"Box"}]""");
        Assert.Equal("Box", Assert.Single(editions).Name);
    }

    [Fact]
    public void Wallet_MillionsGame_SendsBigAmounts()
    {
        var host = WalletGame.Host(new GameSettings { StartMoney = 15_000_000, GoSalary = 2_000_000 }, "Host", "🎩", "red");
        var guest = WalletGame.Join(host.Code, new GameSettings(), "Guest", "🚗", "blue");
        Assert.Equal(15_000_000, guest.Me.Balance);
        var (_, code) = host.Send(12_500_000);
        guest.Receive(code);
        Assert.Equal(27_500_000, guest.Me.Balance);
        var ex = Assert.Throws<BankException>(() => guest.Send(2_097_153));
        Assert.Equal(BankError.AmountTooPrecise, ex.Error);
    }

    [Fact]
    public void Wallet_RejectsRulesThatDontFitTheCode()
    {
        var ex = Assert.Throws<BankException>(() => WalletGame.Host(new GameSettings { GoSalary = 12_345 }, "Host", "🎩", "red"));
        Assert.Equal(BankError.InvalidSettings, ex.Error);
    }
}
