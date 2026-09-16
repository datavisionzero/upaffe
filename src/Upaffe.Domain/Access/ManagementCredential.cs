namespace Upaffe.Domain.Access;

/// <summary>A named authorization for noninteractive management.</summary>
public sealed class ManagementCredential
{
    public const int MaximumNameLength = 100;

    private ManagementCredential()
    {
    }

    private ManagementCredential(Guid operatorId, string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        OperatorId = operatorId;
        Name = AcceptedName(name);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OperatorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ManagementCredential Create(Guid operatorId, string name, DateTimeOffset now)
    {
        if (operatorId == Guid.Empty)
        {
            throw new ArgumentException("An operator is required.", nameof(operatorId));
        }

        return new(operatorId, name, now);
    }

    public void RecordRotation(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("A revoked management credential cannot be rotated.");
        }

        RotatedAt = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static string AcceptedName(string value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 1 or > MaximumNameLength)
        {
            throw new ArgumentException("A management credential name is required.", nameof(value));
        }

        return name;
    }
}
