namespace Upaffe.Domain.Access;

/// <summary>A revocable, time-bounded admission for the operator's browser.</summary>
public sealed class BrowserSession
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);
    public const int MaximumDescriptionLength = 200;

    private BrowserSession()
    {
    }

    private BrowserSession(
        Guid operatorId,
        byte[] secretHash,
        string? description,
        DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OperatorId = operatorId;
        SecretHash = SecretValue.RequiredHash(secretHash, nameof(secretHash));
        Description = ShortDescription(description);
        CreatedAt = now;
        LastUsedAt = now;
        ExpiresAt = now.Add(AbsoluteLifetime);
    }

    public Guid Id { get; private set; }
    public Guid OperatorId { get; private set; }
    public byte[] SecretHash { get; private set; } = [];
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static (BrowserSession Session, string Secret) Begin(
        Guid operatorId,
        string? description,
        DateTimeOffset now)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator is required.", nameof(operatorId));
        }

        var secret = SecretValue.Create();
        return (new BrowserSession(operatorId, secret.Hash, description, now), secret.Secret);
    }

    public bool IsValid(DateTimeOffset now) =>
        RevokedAt is null
        && now < ExpiresAt
        && now - LastUsedAt < IdleLifetime;

    public bool Touch(DateTimeOffset now)
    {
        if (!IsValid(now) || now - LastUsedAt < TouchInterval)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static string? ShortDescription(string? value)
    {
        var description = value?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            return null;
        }

        return description.Length <= MaximumDescriptionLength
            ? description
            : description[..MaximumDescriptionLength];
    }
}
