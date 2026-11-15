using System.Security.Cryptography;

namespace MonopolyBank.Core;

/// <summary>Crockford Base32 (no I, L, O, U). Lenient decode: case-insensitive, ignores '-' and spaces, O→0, I/L→1.</summary>
public static class Base32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static int CharsFor(int bytes) => (bytes * 8 + 4) / 5;

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var chars = new char[CharsFor(data.Length)];
        int buffer = 0, bits = 0, i = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                chars[i++] = Alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }
        if (bits > 0) chars[i] = Alphabet[(buffer << (5 - bits)) & 31];
        return new string(chars);
    }

    public static bool TryDecode(string? text, int byteCount, out byte[] data)
    {
        data = [];
        var clean = Normalize(text);
        if (clean.Length != CharsFor(byteCount)) return false;
        var result = new byte[byteCount];
        int buffer = 0, bits = 0, o = 0;
        foreach (var c in clean)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) return false;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                if (o < byteCount) result[o++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
            buffer &= (1 << bits) - 1;
        }
        if (o != byteCount || buffer != 0) return false; // padding bits must be zero
        data = result;
        return true;
    }

    /// <summary>Encodes the low (chars*5) bits of value, most significant first.</summary>
    public static string EncodeBits(ulong value, int chars)
    {
        var c = new char[chars];
        for (var i = chars - 1; i >= 0; i--) { c[i] = Alphabet[(int)(value & 31)]; value >>= 5; }
        return new string(c);
    }

    public static bool TryDecodeBits(string? text, int chars, out ulong value)
    {
        value = 0;
        var clean = Normalize(text);
        if (clean.Length != chars || chars * 5 > 64) return false;
        foreach (var ch in clean)
        {
            var v = Alphabet.IndexOf(ch);
            if (v < 0) { value = 0; return false; }
            value = value << 5 | (uint)v;
        }
        return true;
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text.ToUpperInvariant())
        {
            switch (ch)
            {
                case '-' or ' ' or '\t': continue;
                case 'O': sb.Append('0'); break;
                case 'I' or 'L': sb.Append('1'); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Splits into dash-separated groups for readability, e.g. ABCDE-FGHIJ.</summary>
    public static string Group(string code, int size = 5) =>
        string.Join('-', Enumerable.Range(0, (code.Length + size - 1) / size)
            .Select(i => code.Substring(i * size, Math.Min(size, code.Length - i * size))));
}

internal static class Checksum
{
    /// <summary>CRC-16/CCITT-FALSE.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    public static void WriteUInt24(Span<byte> dst, uint v) { dst[0] = (byte)(v >> 16); dst[1] = (byte)(v >> 8); dst[2] = (byte)v; }
    public static uint ReadUInt24(ReadOnlySpan<byte> src) => (uint)(src[0] << 16 | src[1] << 8 | src[2]);

    public static uint RandomUInt24() => (uint)RandomNumberGenerator.GetInt32(1, 1 << 24);
}

/// <summary>
/// Amounts in codes: 3-bit power of ten + mantissa ("15 000 000" = 15000 × 10³). Exact for every value below
/// 2^mantissaBits; bigger values must end with enough zeros (true for typical game amounts).
/// </summary>
public static class ScaledAmount
{
    public static bool TryEncode(long value, int mantissaBits, out uint bits)
    {
        bits = 0;
        if (value < 0) return false;
        var exp = 0;
        while (value >= 1L << mantissaBits)
        {
            if (value % 10 != 0 || exp == 7) return false;
            value /= 10;
            exp++;
        }
        bits = (uint)(exp << mantissaBits) | (uint)value;
        return true;
    }

    public static long Decode(uint bits, int mantissaBits)
    {
        var value = (long)(bits & ((1u << mantissaBits) - 1));
        for (var e = bits >> mantissaBits; e > 0; e--) value *= 10;
        return value;
    }
}

/// <summary>
/// Everything another phone needs to join a wallet game: game id + rules. 10 bytes → 16 chars.
/// v2 (current): 24-bit id, start money scaled 3+21 bits, GO salary scaled 3+13 bits, CRC16 ^ salt.
/// v1 (still read): raw 24-bit start money, raw 16-bit GO salary, plain CRC16. The salt makes v1 apps reject v2 codes.
/// </summary>
public readonly record struct GameCode(uint GameId, long StartMoney, long GoSalary)
{
    private const int Bytes = 10;
    private const ushort V2Salt = 0x6B32;

    public static bool CanEncode(long startMoney, long goSalary) =>
        ScaledAmount.TryEncode(startMoney, 21, out _) && ScaledAmount.TryEncode(goSalary, 13, out _);

    public string Encode()
    {
        if (GameId is 0 or >= 1 << 24
            || !ScaledAmount.TryEncode(StartMoney, 21, out var start)
            || !ScaledAmount.TryEncode(GoSalary, 13, out var go))
            throw new BankException(BankError.InvalidSettings);
        Span<byte> b = stackalloc byte[Bytes];
        Checksum.WriteUInt24(b, GameId);
        Checksum.WriteUInt24(b[3..], start);
        b[6] = (byte)(go >> 8);
        b[7] = (byte)go;
        var crc = (ushort)(Checksum.Crc16(b[..8]) ^ V2Salt);
        b[8] = (byte)(crc >> 8);
        b[9] = (byte)crc;
        return Base32.Group(Base32.Encode(b), 4);
    }

    public static bool TryParse(string? text, out GameCode code)
    {
        code = default;
        if (!Base32.TryDecode(text, Bytes, out var b)) return false;
        var id = Checksum.ReadUInt24(b);
        if (id == 0) return false;
        var crc = Checksum.Crc16(b.AsSpan(0, 8));
        var stored = (ushort)(b[8] << 8 | b[9]);
        var start = Checksum.ReadUInt24(b.AsSpan(3));
        var go = (uint)(b[6] << 8 | b[7]);
        if (stored == (ushort)(crc ^ V2Salt)) code = new GameCode(id, ScaledAmount.Decode(start, 21), ScaledAmount.Decode(go, 13));
        else if (stored == crc) code = new GameCode(id, start, go);
        else return false;
        return true;
    }
}

/// <summary>
/// A payment between two wallets: 20-bit transfer id + 24-bit amount + 16-bit CRC keyed with the game id
/// (codes from another game are rejected). 60 bits → 12 chars (XXXX-XXXX-XXXX).
/// v2 (current): amount scaled 3+21 bits, CRC ^ salt. v1 (still read): raw amount, plain CRC.
/// </summary>
public readonly record struct TransferCode(uint TransferId, long Amount)
{
    private const int Chars = 12;
    private const ushort V2Salt = 0x7A52;
    public const uint MaxTransferId = (1 << 20) - 1;

    public static uint NewId() => (uint)RandomNumberGenerator.GetInt32(1, (int)MaxTransferId + 1);

    public static bool CanSend(long amount) => amount is > 0 and <= Limits.MaxAmount && ScaledAmount.TryEncode(amount, 21, out _);

    public string Encode(uint gameId)
    {
        if (Amount is <= 0 or > Limits.MaxAmount) throw new BankException(BankError.InvalidAmount);
        if (!ScaledAmount.TryEncode(Amount, 21, out var amount)) throw new BankException(BankError.AmountTooPrecise);
        if (TransferId is 0 or > MaxTransferId) throw new BankException(BankError.InvalidCode);
        var payload = (ulong)TransferId << 24 | amount;
        var value = payload << 16 | (ushort)(Crc(gameId, payload) ^ V2Salt);
        return Base32.Group(Base32.EncodeBits(value, Chars), 4);
    }

    public static bool TryParse(string? text, uint gameId, out TransferCode code)
    {
        code = default;
        if (!Base32.TryDecodeBits(text, Chars, out var value)) return false;
        var payload = value >> 16;
        var crc = Crc(gameId, payload);
        var raw = (uint)(payload & 0xFFFFFF);
        long amount;
        if ((ushort)value == (ushort)(crc ^ V2Salt)) amount = ScaledAmount.Decode(raw, 21);
        else if ((ushort)value == crc) amount = raw;
        else return false;
        var id = (uint)(payload >> 24);
        if (id == 0 || amount is <= 0 or > Limits.MaxAmount) return false;
        code = new TransferCode(id, amount);
        return true;
    }

    private static ushort Crc(uint gameId, ulong payload)
    {
        Span<byte> buf = stackalloc byte[9];
        Checksum.WriteUInt24(buf, gameId);
        for (var i = 0; i < 6; i++) buf[3 + i] = (byte)(payload >> (40 - 8 * i));
        return Checksum.Crc16(buf);
    }
}

/// <summary>
/// Text inside QR codes. Game invite: join URL ".../join?g=CODE&amp;c=CUR&amp;ca=1" (c/ca = currency and its position, optional)
/// or "MBG1:&lt;code&gt;". Transfer: "MBT1:&lt;code&gt;:&lt;escaped sender name&gt;".
/// </summary>
public static class QrPayload
{
    public const string GamePrefix = "MBG1:";
    public const string TransferPrefix = "MBT1:";

    public static string ForGame(GameCode code) => GamePrefix + code.Encode();

    public static string ForTransfer(string code, string senderName) =>
        TransferPrefix + code + ":" + Uri.EscapeDataString(senderName);

    public static bool TryGetGameCode(string? text, out GameCode code) => TryGetInvite(text, out code, out _);

    /// <summary>Parses a scanned invite, link or typed code. <paramref name="look"/> is set when a link carries the currency.</summary>
    public static bool TryGetInvite(string? text, out GameCode code, out GameSettings? look)
    {
        code = default;
        look = null;
        if (text is null) return false;
        text = text.Trim();
        if (text.StartsWith(GamePrefix, StringComparison.OrdinalIgnoreCase)) text = text[GamePrefix.Length..];
        else if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Query.Length > 1)
        {
            var q = ParseQuery(uri.Query);
            text = q.GetValueOrDefault("g", "");
            look = LookFromQuery(q);
        }
        return GameCode.TryParse(text, out code);
    }

    /// <summary>Reads c (currency) and ca (symbol after) from a join link query.</summary>
    public static GameSettings? LookFromQuery(IReadOnlyDictionary<string, string> q)
    {
        if (!q.TryGetValue("c", out var cur) || cur.Trim().Length > Limits.MaxCurrencyLength) return null;
        return new GameSettings { Currency = cur.Trim(), CurrencyAfter = q.GetValueOrDefault("ca") == "1" };
    }

    public static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')));

    /// <summary>Join link for the game QR: phones without the app open it straight in the browser.</summary>
    public static string JoinUrl(string baseUri, GameCode code, GameSettings? look = null)
    {
        var url = $"{baseUri.TrimEnd('/')}/join?g={code.Encode()}";
        if (look is not null)
            url += $"&c={Uri.EscapeDataString(look.Currency)}{(look.CurrencyAfter ? "&ca=1" : "")}";
        return url;
    }

    /// <summary>Accepts a scanned QR payload or a hand-typed code. Returns the raw code text and optional sender name.</summary>
    public static (string Code, string? Sender) SplitTransfer(string? text)
    {
        text = (text ?? "").Trim();
        if (!text.StartsWith(TransferPrefix, StringComparison.OrdinalIgnoreCase)) return (text, null);
        var rest = text[TransferPrefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon < 0) return (rest, null);
        var name = Uri.UnescapeDataString(rest[(colon + 1)..]).Trim();
        if (name.Length > Limits.MaxNameLength) name = name[..Limits.MaxNameLength];
        return (rest[..colon], name.Length == 0 ? null : name);
    }
}
