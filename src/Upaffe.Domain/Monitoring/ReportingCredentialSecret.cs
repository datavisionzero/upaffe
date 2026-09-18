using Upaffe.Domain.Access;

namespace Upaffe.Domain.Monitoring;

/// <summary>One hashed secret belonging to a reporting credential.</summary>
public sealed class ReportingCredentialSecret
{
    public static readonly TimeSpan RotationOverlap = TimeSpan.FromMinutes(5);

    private ReportingCredentialSecret()
    {
    }

    private ReportingCredentialSecret(Guid credentialId, byte[] secretHash, DateTimeOffset now)
    {
        if (credentialId == Guid.Empty)
        {
            throw new ArgumentException("A reporting credential is required.", nameof(credentialId));
        }

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

    public static (ReportingCredentialSecret Secret, string Value) Issue(Guid credentialId, DateTimeOffset now)
    {
        var generated = SecretValue.Create();
        var value = $"uar_{generated.Secret}";
        return (new ReportingCredentialSecret(credentialId, SecretValue.Hash(value), now), value);
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
