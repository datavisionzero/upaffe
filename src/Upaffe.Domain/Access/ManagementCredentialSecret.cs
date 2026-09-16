namespace Upaffe.Domain.Access;

/// <summary>One hashed secret belonging to a management credential.</summary>
public sealed class ManagementCredentialSecret
{
    private ManagementCredentialSecret()
    {
    }

    private ManagementCredentialSecret(Guid credentialId, byte[] secretHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CredentialId = credentialId;
        SecretHash = SecretValue.RequiredHash(secretHash, nameof(secretHash));
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid CredentialId { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public static (ManagementCredentialSecret Secret, string Value) Issue(
        Guid credentialId,
        DateTimeOffset now)
    {
        if (credentialId == Guid.Empty)
        {
            throw new ArgumentException("A management credential is required.", nameof(credentialId));
        }

        var generated = SecretValue.Create();
        return (new ManagementCredentialSecret(credentialId, generated.Hash, now), generated.Secret);
    }

    public bool IsValid(DateTimeOffset now) => ExpiresAt is null || now < ExpiresAt;

    public void ExpireAt(DateTimeOffset expiresAt)
    {
        if (expiresAt <= CreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        ExpiresAt = expiresAt;
    }
}
