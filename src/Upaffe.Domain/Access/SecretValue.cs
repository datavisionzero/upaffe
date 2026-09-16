using System.Security.Cryptography;

namespace Upaffe.Domain.Access;

/// <summary>Creates opaque secrets and the only form allowed into persistence.</summary>
public static class SecretValue
{
    public const int ByteCount = 32;
    public const int HashByteCount = 32;

    public static (string Secret, byte[] Hash) Create()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(ByteCount))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return (secret, Hash(secret));
    }

    public static byte[] Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
    }

    public static byte[] RequiredHash(byte[] hash, string parameter)
    {
        ArgumentNullException.ThrowIfNull(hash, parameter);
        if (hash.Length != HashByteCount)
        {
            throw new ArgumentException($"A secret hash must be {HashByteCount} bytes.", parameter);
        }

        return hash.ToArray();
    }

    public static bool Matches(byte[] expected, byte[] candidate) =>
        expected.Length == candidate.Length
        && CryptographicOperations.FixedTimeEquals(expected, candidate);
}
