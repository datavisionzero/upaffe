namespace Upaffe.Domain.Access;

/// <summary>The parseable public identifier and opaque secret presented by automation.</summary>
public static class ManagementToken
{
    private const string Prefix = "upaffe_";
    private const int IdentifierLength = 32;
    private const int SecretLength = 43;

    public static string Format(Guid credentialId, string secret)
    {
        if (credentialId == Guid.Empty || !IsSecret(secret))
        {
            throw new ArgumentException("A management credential identifier and secret are required.");
        }

        return $"{Prefix}{credentialId:N}_{secret}";
    }

    public static bool TryParse(string? token, out Guid credentialId, out byte[] secretHash)
    {
        credentialId = Guid.Empty;
        secretHash = [];
        if (token is null
            || token.Length != Prefix.Length + IdentifierLength + 1 + SecretLength
            || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var identifier = token.AsSpan(Prefix.Length, IdentifierLength);
        var separator = Prefix.Length + IdentifierLength;
        var secret = token[(separator + 1)..];
        if (token[separator] != '_'
            || !Guid.TryParseExact(identifier, "N", out credentialId)
            || credentialId == Guid.Empty
            || !IsSecret(secret))
        {
            credentialId = Guid.Empty;
            return false;
        }

        secretHash = SecretValue.Hash(secret);
        return true;
    }

    private static bool IsSecret(string value) =>
        value.Length == SecretLength
        && value.All(character =>
            character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_');
}
