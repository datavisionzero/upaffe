namespace Upaffe.Domain.Access;

/// <summary>A short-lived, one-use proof allowed only before an operator exists.</summary>
public sealed class BootstrapGrant
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private BootstrapGrant()
    {
    }

    private BootstrapGrant(byte[] secretHash, DateTimeOffset armedAt)
    {
        Id = Guid.NewGuid();
        IsSingleton = true;
        SecretHash = SecretValue.RequiredHash(secretHash, nameof(secretHash));
        ArmedAt = armedAt;
        ExpiresAt = armedAt.Add(Lifetime);
    }

    public Guid Id { get; private set; }
    public bool IsSingleton { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public DateTimeOffset ArmedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static BootstrapGrant Arm(byte[] secretHash, DateTimeOffset now) => new(secretHash, now);

    public bool Accepts(byte[] candidateHash, DateTimeOffset now) =>
        ConsumedAt is null
        && now < ExpiresAt
        && SecretValue.Matches(SecretHash, candidateHash);

    public void Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null || now >= ExpiresAt)
        {
            throw new InvalidOperationException("The bootstrap grant is no longer usable.");
        }

        ConsumedAt = now;
    }
}
