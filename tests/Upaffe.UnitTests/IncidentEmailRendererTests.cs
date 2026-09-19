using Upaffe.Application.Notifications;

namespace Upaffe.UnitTests;

public sealed class IncidentEmailRendererTests
{
    [Fact]
    public void Alert_and_recovery_include_stable_facts_and_escape_untrusted_names()
    {
        var id = Guid.NewGuid();
        var facts = new IncidentEmailFacts(id, "alert", "backup-jobs", "<script>project</script>",
            "nightly", "<b>monitor</b>", "push", "reported_failure",
            new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero), "ops@example.test");
        var alert = IncidentEmailRenderer.Render(facts, "https://status.example.test");
        var repeated = IncidentEmailRenderer.Render(facts, "https://status.example.test");
        Assert.Contains("backup-jobs/nightly (reported_failure)", alert.Subject);
        Assert.DoesNotContain("<script>", alert.Subject);
        Assert.Contains("&lt;script&gt;project&lt;/script&gt;", alert.HtmlBody);
        Assert.Contains("&lt;b&gt;monitor&lt;/b&gt;", alert.HtmlBody);
        Assert.Contains("2026-09-19 12:00:00 UTC", alert.TextBody);
        Assert.Contains($"https://status.example.test/projects/backup-jobs/push-monitors/nightly?incident={id:D}", alert.TextBody);
        Assert.Equal(alert.MessageId, repeated.MessageId);

        var recovery = IncidentEmailRenderer.Render(facts with { Kind = "recovery" }, null);
        Assert.Contains("recovery", recovery.Subject);
        Assert.Contains("/projects/backup-jobs/push-monitors/nightly", recovery.TextBody);
        Assert.NotEqual(alert.MessageId, recovery.MessageId);
    }

    [Fact]
    public void Unknown_reason_cannot_put_remote_text_in_subject()
    {
        var facts = new IncidentEmailFacts(Guid.NewGuid(), "alert", "website", "Website",
            "home", "Home", "http", "secret response body", DateTimeOffset.UtcNow,
            "ops@example.test");
        var email = IncidentEmailRenderer.Render(facts, null);
        Assert.Contains("monitor_failure", email.Subject);
        Assert.DoesNotContain("secret response body", email.Subject);
    }
}
