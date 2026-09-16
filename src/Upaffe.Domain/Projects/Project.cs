using System.Text.RegularExpressions;

namespace Upaffe.Domain.Projects;

/// <summary>A persistent organizational boundary for monitoring.</summary>
public sealed partial class Project
{
    public const int MaximumKeyLength = 40;
    public const int MaximumNameLength = 100;

    private Project()
    {
    }

    private Project(string key, string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Key = ValidateKey(key);
        Name = ValidateName(name);
        Version = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static Project Create(string key, string name, DateTimeOffset now) => new(key, name, now);

    public void Rename(string name, DateTimeOffset now)
    {
        EnsureLive();
        Name = ValidateName(name);
        Changed(now);
    }

    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        DeletedAt = now;
        Changed(now);
    }

    public void Restore(DateTimeOffset now)
    {
        if (DeletedAt is null)
        {
            return;
        }

        DeletedAt = null;
        Changed(now);
    }

    private void EnsureLive()
    {
        if (DeletedAt is not null)
        {
            throw new InvalidOperationException("A deleted project must be restored before it changes.");
        }
    }

    private void Changed(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    public static string ValidateKey(string value)
    {
        var key = (value ?? string.Empty).Trim();
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException(
                "A project key must be 2-40 lower-case letters, digits, or hyphens and start with a letter.",
                nameof(value));
        }

        return key;
    }

    public static string ValidateName(string value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length is < 1 or > MaximumNameLength)
        {
            throw new ArgumentException("A project name must be 1-100 characters.", nameof(value));
        }

        return name;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
