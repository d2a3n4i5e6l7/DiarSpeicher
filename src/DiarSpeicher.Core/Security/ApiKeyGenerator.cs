using System.Security.Cryptography;

namespace DiarSpeicher.Core.Security;

/// <summary>
/// API keys are 256 bits of cryptographic randomness, so unlike passwords they need no
/// cost factor: brute forcing the keyspace is infeasible and a slow hash would only tax
/// every authenticated request. Only <see cref="HashKey"/> is persisted; the plaintext is
/// shown to the caller once at issue time.
/// </summary>
public static class ApiKeyGenerator
{
    private const int KeyBytes = 32;

    public static string GenerateKey() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(KeyBytes));

    public static string HashKey(string key) =>
        Base64UrlEncode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
