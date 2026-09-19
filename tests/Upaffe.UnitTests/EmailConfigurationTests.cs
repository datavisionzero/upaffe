using Upaffe.Domain.Notifications;

namespace Upaffe.UnitTests;

public sealed class EmailConfigurationTests
{
    [Fact]
    public void Recipients_are_normalized_without_silently_dropping_invalid_or_duplicate_addresses()
    {
        Assert.Equal(["Alerts@example.test", "ops@example.test"],
            EmailConfiguration.NormalizeRecipients([" Alerts@Example.TEST ", "ops@example.test"]));
        Assert.Throws<ArgumentException>(() => EmailConfiguration.NormalizeRecipients(
            ["Alerts@example.test", "alerts@EXAMPLE.TEST"]));
        Assert.Throws<ArgumentException>(() => EmailConfiguration.NormalizeRecipients(
            ["Name <alerts@example.test>"]));
    }
}
