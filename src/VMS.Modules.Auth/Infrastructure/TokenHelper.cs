using System.Security.Cryptography;
using System.Text;

namespace VMS.Modules.Auth.Infrastructure;

internal static class TokenHelper
{
    /// <summary>A URL-safe random token with 256 bits of entropy.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>A uniformly random 6-digit code (no modulo bias).</summary>
    public static string NewSixDigitCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    public static bool HashesEqual(string? a, string? b) =>
        a is not null && b is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
