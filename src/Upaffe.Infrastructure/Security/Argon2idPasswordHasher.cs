using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Infrastructure.Security;

/// <summary>Argon2id in a bounded, self-describing PHC value.</summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MemoryKiB = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 1;
    private const int HashBytes = 32;
    private const int SaltBytes = 16;

    public Task<string> HashAsync(string password, CancellationToken cancellationToken) =>
        EncodeAsync(
            Password.Validate(password),
            RandomNumberGenerator.GetBytes(SaltBytes),
            MemoryKiB,
            Iterations,
            Parallelism,
            cancellationToken);

    public async Task<bool> VerifyAsync(
        string encodedHash,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(encodedHash))
        {
            return false;
        }

        try
        {
            var parts = encodedHash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
            {
                return false;
            }

            var parameters = parts[3]
                .Split(',')
                .Select(pair => pair.Split('='))
                .ToDictionary(
                    pair => pair[0],
                    pair => int.Parse(pair[1], CultureInfo.InvariantCulture));
            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);

            if (parameters["m"] is < 8192 or > 262144
                || parameters["t"] is < 1 or > 10
                || parameters["p"] is < 1 or > 16
                || salt.Length is < 16 or > 64
                || expected.Length != HashBytes)
            {
                return false;
            }

            var recomputed = await EncodeAsync(
                password,
                salt,
                parameters["m"],
                parameters["t"],
                parameters["p"],
                cancellationToken);
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(recomputed.Split('$')[5]),
                expected);
        }
        catch (Exception exception) when (
            exception is FormatException
                or ArgumentException
                or KeyNotFoundException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static async Task<string> EncodeAsync(
        string password,
        byte[] salt,
        int memory,
        int iterations,
        int parallelism,
        CancellationToken cancellationToken)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        cancellationToken.ThrowIfCancellationRequested();
        var hash = await argon.GetBytesAsync(HashBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={memory},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }
}
