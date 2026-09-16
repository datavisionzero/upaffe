namespace Upaffe.Infrastructure.Persistence;

public static class SchemaVersions
{
    public static IReadOnlyList<string> NotKnownHere(
        IEnumerable<string> applied,
        IEnumerable<string> known) =>
        [.. applied.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

/// <summary>Raised when an older binary sees migrations it cannot understand.</summary>
public sealed class SchemaIsNewerException(IReadOnlyList<string> migrations) : Exception(Describe(migrations))
{
    public IReadOnlyList<string> Migrations { get; } = migrations;

    private static string Describe(IReadOnlyList<string> migrations) =>
        "This database was migrated by a newer upaffe. It carries "
        + $"{migrations.Count} unknown migration(s): {string.Join(", ", migrations)}. "
        + "Start the version that migrated it or restore the pre-upgrade backup; "
        + "there is no downgrade path.";
}
