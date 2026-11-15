using QRCoder;

namespace MonopolyBank.Services;

public static class QrService
{
    /// <summary>PNG data URI of a QR code (generated locally, works offline).</summary>
    public static string DataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8, [0x1A, 0x1A, 0x1A], [0xFF, 0xFF, 0xFF], drawQuietZones: true);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }
}
