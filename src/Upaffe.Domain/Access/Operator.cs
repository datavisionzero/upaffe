namespace Upaffe.Domain.Access;

/// <summary>The one human responsible for an upaffe instance.</summary>
public sealed class Operator
{
    public const int MaximumEmailLength = 320;
    public const int MaximumPasswordHashLength = 512;

    private Operator()
    {
    }

    private Operator(Guid id, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        IsSingleton = true;
        Email = AcceptedEmail(email);
        NormalizedEmail = NormalizeEmail(Email);
        PasswordHash = AcceptedPasswordHash(passwordHash);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public bool IsSingleton { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Operator Establish(string email, string passwordHash, DateTimeOffset now) =>
        new(Guid.NewGuid(), email, passwordHash, now);

    public void Recover(string email, string passwordHash, DateTimeOffset now)
    {
        Email = AcceptedEmail(email);
        NormalizedEmail = NormalizeEmail(Email);
        PasswordHash = AcceptedPasswordHash(passwordHash);
        UpdatedAt = now;
    }

    public static string NormalizeEmail(string email) => AcceptedEmail(email).ToUpperInvariant();

    private static string AcceptedEmail(string value)
    {
        var email = (value ?? string.Empty).Trim();
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (email.Length is < 3 or > MaximumEmailLength
            || at < 1
            || at != email.LastIndexOf('@')
            || at == email.Length - 1)
        {
            throw new ArgumentException("A valid operator email address is required.", nameof(value));
        }

        return email;
    }

    private static string AcceptedPasswordHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPasswordHashLength)
        {
            throw new ArgumentException("A bounded password hash is required.", nameof(value));
        }

        return value;
    }
}
