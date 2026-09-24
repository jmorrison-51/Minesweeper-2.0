using System.Security.Cryptography;
using System.Text;

namespace Minesweeper.Core;

/// <summary>
/// Light protection for save files: AES-CBC with an HMAC over the result, so the file is unreadable in a
/// text editor and any edit (or a plain-text replacement) is rejected. The keys are derived from a phrase
/// built into the program, so this stops casual tampering, not someone willing to take the exe apart.
/// </summary>
internal static class SaveCrypto
{
    private static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'2', 1 };
    private static readonly byte[] EncryptionKey = Derive("Minesweeper2.save.encryption");
    private static readonly byte[] MacKey = Derive("Minesweeper2.save.integrity");

    private const int IvSize = 16;
    private const int MacSize = 32;

    private static byte[] Derive(string purpose) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(purpose + "|v1|mines-and-hexes"));

    public static byte[] Encrypt(byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = EncryptionKey;
        aes.GenerateIV();
        byte[] cipher = aes.EncryptCbc(plain, aes.IV);

        var body = new byte[Magic.Length + IvSize + cipher.Length + MacSize];
        Magic.CopyTo(body, 0);
        aes.IV.CopyTo(body, Magic.Length);
        cipher.CopyTo(body, Magic.Length + IvSize);

        int signedLength = body.Length - MacSize;
        HMACSHA256.HashData(MacKey, body.AsSpan(0, signedLength)).CopyTo(body, signedLength);
        return body;
    }

    /// <summary>False if the data is not a save file, was altered, or cannot be decrypted.</summary>
    public static bool TryDecrypt(byte[] data, out byte[] plain)
    {
        plain = Array.Empty<byte>();
        if (data.Length < Magic.Length + IvSize + 16 + MacSize) return false;
        if (!data.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return false;

        int signedLength = data.Length - MacSize;
        byte[] expected = HMACSHA256.HashData(MacKey, data.AsSpan(0, signedLength));
        if (!CryptographicOperations.FixedTimeEquals(expected, data.AsSpan(signedLength))) return false;

        try
        {
            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            byte[] iv = data.AsSpan(Magic.Length, IvSize).ToArray();
            plain = aes.DecryptCbc(data.AsSpan(Magic.Length + IvSize, signedLength - Magic.Length - IvSize), iv);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
