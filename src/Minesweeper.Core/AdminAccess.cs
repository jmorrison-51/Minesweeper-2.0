using System.Security.Cryptography;
using System.Text;

namespace Minesweeper.Core;

/// <summary>
/// The "admin" login, for testing: it opens a separate profile with Endless Mode unlocked.
/// Only a hash of the password is kept here. To change the password, replace <see cref="PasswordSha256"/>
/// with the lowercase hex SHA-256 of the new one (PowerShell:
/// <c>[BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("new"))).Replace("-","").ToLower()</c>).
/// </summary>
public static class AdminAccess
{
    public const string UserName = "admin";

    private const string PasswordSha256 = "6d92380823f0085e08affba53b31dbed0475afde038f83f37e825dc2e9036e5e";

    public static bool IsAdminName(string? name) =>
        string.Equals(name?.Trim(), UserName, StringComparison.OrdinalIgnoreCase);

    public static bool PasswordMatches(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(PasswordSha256));
    }
}
