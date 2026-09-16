using Upaffe.Application.Ports;

namespace Upaffe.UnitTests;

public sealed class DatabaseSettingsTests
{
    [Fact]
    public void Missing_configuration_names_the_required_variable()
    {
        var failure = Assert.Throws<ArgumentException>(
            () => DatabaseSettings.FromConnectionString(null));
        Assert.Contains(DatabaseSettings.Variable, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_connection_string_without_a_host_is_refused() =>
        Assert.Throws<ArgumentException>(
            () => DatabaseSettings.FromConnectionString("Database=upaffe;Username=upaffe"));

    [Fact]
    public void Credentials_are_redacted()
    {
        var settings = DatabaseSettings.FromConnectionString(
            "Host=db;Database=upaffe;Username=upaffe;Password=not-for-output");
        Assert.DoesNotContain("not-for-output", settings.Redacted, StringComparison.Ordinal);
        Assert.Contains("Password=***", settings.Redacted, StringComparison.Ordinal);
    }
}
