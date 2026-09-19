using Upaffe.Domain.Notifications;

namespace Upaffe.UnitTests;

public sealed class MaintenanceWindowTests
{
    [Fact]
    public void Extending_never_shortens_and_early_end_takes_effect_immediately()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var window = MaintenanceWindow.Start("project", Guid.NewGuid(), 1,
            now, TimeSpan.FromMinutes(20));
        window.Extend(now.AddMinutes(5), TimeSpan.FromMinutes(10));
        Assert.Equal(now.AddMinutes(20), window.EndsAt);
        Assert.Equal(2, window.Version);
        window.Extend(now.AddMinutes(6), TimeSpan.FromMinutes(30));
        Assert.Equal(now.AddMinutes(36), window.EndsAt);
        window.End(now.AddMinutes(7));
        Assert.False(window.IsActive(now.AddMinutes(7)));
        Assert.Equal(4, window.Version);
        Assert.Throws<ArgumentException>(() => MaintenanceWindow.ValidateDuration(TimeSpan.FromSeconds(59)));
        Assert.Throws<ArgumentException>(() => MaintenanceWindow.ValidateDuration(TimeSpan.FromDays(31)));
    }
}
