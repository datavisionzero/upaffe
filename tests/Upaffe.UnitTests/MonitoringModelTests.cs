using Upaffe.Domain.Monitoring;

namespace Upaffe.UnitTests;

public sealed class MonitoringModelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_monitor_is_due_and_keeps_target_secrets_out_of_ordinary_state()
    {
        var projectId = Guid.NewGuid();
        var monitor = NewMonitor(projectId, "https://status.example.test/health?token=secret");
        var secret = HttpMonitorSecret.FromTarget(monitor.Id, "https://status.example.test/health?token=secret");

        Assert.Equal(MonitorState.Untested, monitor.State);
        Assert.Equal(Noon, monitor.NextCheckAt);
        Assert.Equal("https://status.example.test/health", monitor.TargetUrl);
        Assert.True(monitor.HasTargetQuery);
        Assert.DoesNotContain("secret", monitor.TargetUrl, StringComparison.Ordinal);
        Assert.Equal("?token=secret", secret.RevealTargetQuery());
    }

    [Fact]
    public void Header_metadata_is_separate_from_its_explicit_secret()
    {
        var monitor = NewMonitor(Guid.NewGuid());
        var (header, secret) = HttpMonitorHeader.Create(monitor.Id, "Authorization", "Bearer secret", Noon);

        Assert.Equal("authorization", header.Name);
        Assert.Equal("Bearer secret", secret.Reveal());
        Assert.DoesNotContain("secret", header.Name, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => HttpMonitorHeader.Create(monitor.Id, "Host", "example.test", Noon));
        Assert.Throws<ArgumentException>(() => secret.Replace("line\nbreak"));
    }

    [Fact]
    public void Pause_and_resume_preserve_history_but_start_a_fresh_generation()
    {
        var monitor = NewMonitor(Guid.NewGuid());
        var success = monitor.BeginCheck(CheckTrigger.Scheduled, Noon, Noon);
        Claim(success, Noon);
        success.CompleteSuccess(Noon.AddSeconds(1), 200, 50, "https://status.example.test/health?ignored=1");
        Assert.True(monitor.ApplyResult(success, Noon.AddSeconds(1)));

        monitor.Pause(Noon.AddMinutes(1));
        Assert.Equal(MonitorState.Paused, monitor.State);
        Assert.Null(monitor.NextCheckAt);
        Assert.Equal(success.Id, monitor.LatestSuccessId);

        monitor.Resume(Noon.AddMinutes(2));
        Assert.Equal(MonitorState.Untested, monitor.State);
        Assert.Equal(2, monitor.EvaluationGeneration);
        Assert.Equal(success.Id, monitor.LatestResultId);
        Assert.Equal(success.Id, monitor.LatestSuccessId);
        Assert.Equal(Noon.AddMinutes(2), monitor.NextCheckAt);
    }

    [Fact]
    public void Late_and_repeated_results_cannot_replace_newer_facts()
    {
        var monitor = NewMonitor(Guid.NewGuid());
        var older = monitor.BeginCheck(CheckTrigger.Requested, Noon, Noon);
        var newer = monitor.BeginCheck(CheckTrigger.Requested, Noon, Noon);
        newer.CompleteFailure("unexpected_status", Noon.AddSeconds(2), 503, 20, "https://status.example.test/health");
        older.CompleteSuccess(Noon.AddSeconds(3), 200, 30, "https://status.example.test/health");

        Assert.True(monitor.ApplyResult(newer, Noon.AddSeconds(2)));
        Assert.False(monitor.ApplyResult(older, Noon.AddSeconds(3)));
        Assert.False(monitor.ApplyResult(newer, Noon.AddSeconds(4)));
        Assert.Equal(MonitorState.Failing, monitor.State);
        Assert.Equal(newer.Id, monitor.LatestResultId);
        Assert.Null(monitor.LatestSuccessId);
    }

    [Fact]
    public void Scheduled_work_has_one_replaceable_lease_and_does_not_replay_missed_intervals()
    {
        var monitor = NewMonitor(Guid.NewGuid());
        var lateStart = Noon.AddMinutes(7);
        var check = monitor.BeginCheck(CheckTrigger.Scheduled, Noon, lateStart);
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();

        check.ClaimExecution(firstToken, lateStart, lateStart.AddMinutes(2));
        check.ClaimExecution(secondToken, lateStart.AddMinutes(3), lateStart.AddMinutes(5));

        Assert.Equal(lateStart.AddMinutes(5), monitor.NextCheckAt);
        Assert.False(check.IsClaimedBy(firstToken));
        Assert.True(check.IsClaimedBy(secondToken));
        Assert.Equal(2, check.ExecutionAttempts);
        Assert.Equal(lateStart.AddMinutes(3), check.LastExecutionAttemptAt);
    }

    [Fact]
    public void Requested_checks_cannot_acquire_scheduler_leases()
    {
        var monitor = NewMonitor(Guid.NewGuid());
        var check = monitor.BeginCheck(CheckTrigger.Requested, Noon, Noon);

        Assert.Throws<InvalidOperationException>(() =>
            check.ClaimExecution(Guid.NewGuid(), Noon, Noon.AddMinutes(2)));
    }

    [Fact]
    public void An_incident_keeps_its_beginning_and_resolves_only_with_fresh_success()
    {
        var monitor = NewMonitor(Guid.NewGuid());
        var first = FailedCheck(monitor, Noon, "timeout");
        var opening = FailedCheck(monitor, Noon.AddSeconds(2), "unexpected_status");
        var incident = Incident.Open(first, opening, Noon.AddSeconds(4));

        Assert.Equal(first.CompletedAt, incident.BeganAt);
        Assert.Equal("timeout", incident.OriginalReason);
        Assert.Equal("unexpected_status", incident.LatestReason);

        var success = monitor.BeginCheck(CheckTrigger.Scheduled, Noon.AddSeconds(5), Noon.AddSeconds(5));
        Claim(success, Noon.AddSeconds(5));
        success.CompleteSuccess(Noon.AddSeconds(6), 200, 10, "https://status.example.test/health");
        incident.Resolve(success);

        Assert.False(incident.IsOpen);
        Assert.Equal(success.Id, incident.ResolutionCheckId);
    }

    [Theory]
    [InlineData(29, 10, 3)]
    [InlineData(60, 61, 3)]
    [InlineData(60, 10, 0)]
    public void Monitor_limits_are_domain_rules(int intervalSeconds, int timeoutSeconds, int threshold) =>
        Assert.ThrowsAny<ArgumentException>(() => HttpMonitor.Create(
            Guid.NewGuid(),
            "public-site",
            "Public site",
            "https://status.example.test/health",
            200,
            TextCondition.None,
            null,
            intervalSeconds,
            timeoutSeconds,
            threshold,
            null,
            null,
            Noon));

    private static HttpMonitor NewMonitor(Guid projectId, string target = "https://status.example.test/health") =>
        HttpMonitor.Create(
            projectId,
            "public-site",
            "Public site",
            target,
            200,
            TextCondition.Required,
            "ready",
            300,
            10,
            3,
            "Check the deployment.",
            "https://runbooks.example.test/public-site",
            Noon);

    private static HttpCheck FailedCheck(HttpMonitor monitor, DateTimeOffset at, string reason)
    {
        var check = monitor.BeginCheck(CheckTrigger.Scheduled, at, at);
        Claim(check, at);
        check.CompleteFailure(reason, at.AddSeconds(1), null, 1_000, null);
        return check;
    }

    private static void Claim(HttpCheck check, DateTimeOffset at) =>
        check.ClaimExecution(Guid.NewGuid(), at, at.AddMinutes(2));
}
