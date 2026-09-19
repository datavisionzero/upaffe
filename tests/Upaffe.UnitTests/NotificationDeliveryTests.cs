using Upaffe.Domain.Notifications;

namespace Upaffe.UnitTests;

public sealed class NotificationDeliveryTests
{
    [Fact]
    public void Transient_failures_back_off_four_times_then_stop()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var delivery = New(now);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var token = Guid.NewGuid();
            delivery.Claim(token, now, TimeSpan.FromMinutes(2));
            delivery.BeginAttempt(token, now);
            delivery.Complete(token, false, true, "smtp_timeout", now);
            Assert.Equal(attempt, delivery.AttemptCount);
            Assert.Equal(now, delivery.LastAttemptAt);
            if (attempt < 5)
            {
                Assert.Equal(DeliveryState.Retrying, delivery.State);
                Assert.Equal(now.AddMinutes(1 << (attempt - 1)), delivery.NextAttemptAt);
                now = delivery.NextAttemptAt;
            }
        }
        Assert.Equal(DeliveryState.TerminalFailure, delivery.State);
        Assert.Equal("smtp_timeout", delivery.LastErrorCode);
    }

    [Fact]
    public void Acceptance_and_permanent_failure_are_distinct_terminal_states()
    {
        var now = DateTimeOffset.UtcNow;
        var accepted = New(now);
        var token = Guid.NewGuid();
        accepted.Claim(token, now, TimeSpan.FromMinutes(2));
        accepted.BeginAttempt(token, now);
        accepted.Complete(token, true, false, null, now);
        Assert.Equal(DeliveryState.Accepted, accepted.State);
        Assert.Equal(now, accepted.AcceptedAt);
        Assert.Null(accepted.LastErrorCode);

        var failed = New(now);
        token = Guid.NewGuid();
        failed.Claim(token, now, TimeSpan.FromMinutes(2));
        failed.BeginAttempt(token, now);
        failed.Complete(token, false, false, "private relay text", now);
        Assert.Equal(DeliveryState.TerminalFailure, failed.State);
        Assert.Equal("smtp_failed", failed.LastErrorCode);
        Assert.Equal(now, failed.TerminalAt);
        failed.Obsolete(now);
        Assert.Equal(DeliveryState.TerminalFailure, failed.State);
    }

    [Fact]
    public void Repeated_expired_claims_count_as_attempts_and_eventually_fail_terminally()
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = New(now);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(delivery.Claim(Guid.NewGuid(), now, TimeSpan.FromMinutes(1)));
            delivery.BeginAttempt(delivery.LeaseToken!.Value, now);
            Assert.Equal(attempt, delivery.AttemptCount);
            now = now.AddMinutes(2);
        }
        Assert.False(delivery.Claim(Guid.NewGuid(), now, TimeSpan.FromMinutes(1)));
        Assert.Equal(DeliveryState.TerminalFailure, delivery.State);
        Assert.Equal("smtp_outcome_unknown", delivery.LastErrorCode);
    }

    private static NotificationDelivery New(DateTimeOffset now) =>
        NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "ops@example.test", "project", "Project", "monitor", "Monitor",
            "http", "timeout", now, now);
}
