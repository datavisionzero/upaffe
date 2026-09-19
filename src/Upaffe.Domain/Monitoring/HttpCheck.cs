using System.Text.RegularExpressions;

namespace Upaffe.Domain.Monitoring;

/// <summary>One ordered attempt and its optional immutable result.</summary>
public sealed partial class HttpCheck
{
    public const int MaximumReasonLength = 64;
    public const int MaximumEffectiveUrlLength = 2_048;

    private HttpCheck()
    {
    }

    private HttpCheck(
        Guid monitorId,
        long evaluationGeneration,
        long sequence,
        CheckTrigger trigger,
        DateTimeOffset scheduledFor,
        DateTimeOffset startedAt)
    {
        if (monitorId == Guid.Empty)
        {
            throw new ArgumentException("A monitor is required.", nameof(monitorId));
        }

        if (evaluationGeneration < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(evaluationGeneration));
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        if (startedAt < scheduledFor)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAt));
        }

        Id = Guid.NewGuid();
        MonitorId = monitorId;
        EvaluationGeneration = evaluationGeneration;
        Sequence = sequence;
        Trigger = trigger;
        ScheduledFor = scheduledFor;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public long EvaluationGeneration { get; private set; }
    public long Sequence { get; private set; }
    public CheckTrigger Trigger { get; private set; }
    public DateTimeOffset ScheduledFor { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public CheckOutcome? Outcome { get; private set; }
    public string? FailureReason { get; private set; }
    public int? StatusCode { get; private set; }
    public int? ResponseTimeMilliseconds { get; private set; }
    public string? EffectiveUrl { get; private set; }
    public bool? AppliedToCurrentState { get; private set; }
    public Guid? ExecutionLeaseToken { get; private set; }
    public DateTimeOffset? ExecutionLeaseUntil { get; private set; }
    public int ExecutionAttempts { get; private set; }
    public DateTimeOffset? LastExecutionAttemptAt { get; private set; }
    public bool IsCompleted => Outcome is not null;

    public void RecordApplicability(bool applied)
    {
        if (!IsCompleted) throw new InvalidOperationException("Only a completed check can be evaluated.");
        if (AppliedToCurrentState is not null) throw new InvalidOperationException("Check applicability is immutable.");
        AppliedToCurrentState = applied;
    }

    public static HttpCheck Begin(
        Guid monitorId,
        long evaluationGeneration,
        long sequence,
        CheckTrigger trigger,
        DateTimeOffset scheduledFor,
        DateTimeOffset startedAt) =>
        new(monitorId, evaluationGeneration, sequence, trigger, scheduledFor, startedAt);

    public void ClaimExecution(Guid token, DateTimeOffset claimedAt, DateTimeOffset leaseUntil)
    {
        if (Trigger != CheckTrigger.Scheduled)
        {
            throw new InvalidOperationException("Only scheduled checks use execution leases.");
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("A completed check cannot be claimed.");
        }

        if (token == Guid.Empty)
        {
            throw new ArgumentException("An execution lease token is required.", nameof(token));
        }

        if (claimedAt < StartedAt || leaseUntil <= claimedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseUntil));
        }

        if (ExecutionLeaseUntil is not null && claimedAt < ExecutionLeaseUntil)
        {
            throw new InvalidOperationException("An active execution lease cannot be replaced.");
        }

        ExecutionLeaseToken = token;
        ExecutionLeaseUntil = leaseUntil;
        ExecutionAttempts++;
        LastExecutionAttemptAt = claimedAt;
    }

    public bool IsClaimedBy(Guid token) =>
        token != Guid.Empty && ExecutionLeaseToken == token;

    public void CompleteSuccess(
        DateTimeOffset completedAt,
        int statusCode,
        int responseTimeMilliseconds,
        string effectiveUrl) =>
        Complete(
            CheckOutcome.Success,
            null,
            completedAt,
            statusCode,
            responseTimeMilliseconds,
            effectiveUrl);

    public void CompleteFailure(
        string reason,
        DateTimeOffset completedAt,
        int? statusCode,
        int? responseTimeMilliseconds,
        string? effectiveUrl) =>
        Complete(
            CheckOutcome.Failure,
            ValidateReason(reason),
            completedAt,
            statusCode,
            responseTimeMilliseconds,
            effectiveUrl);

    private void Complete(
        CheckOutcome outcome,
        string? reason,
        DateTimeOffset completedAt,
        int? statusCode,
        int? responseTimeMilliseconds,
        string? effectiveUrl)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("A check result is immutable once completed.");
        }

        if (Trigger == CheckTrigger.Scheduled && ExecutionLeaseToken is null)
        {
            throw new InvalidOperationException("A scheduled check must be claimed before completion.");
        }

        if (completedAt < StartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAt));
        }

        if (statusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        if (responseTimeMilliseconds is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(responseTimeMilliseconds));
        }

        var acceptedUrl = NormalizeEffectiveUrl(effectiveUrl);
        Outcome = outcome;
        FailureReason = reason;
        CompletedAt = completedAt;
        StatusCode = statusCode;
        ResponseTimeMilliseconds = responseTimeMilliseconds;
        EffectiveUrl = acceptedUrl;
    }

    private static string ValidateReason(string value)
    {
        var reason = (value ?? string.Empty).Trim();
        if (!ReasonPattern().IsMatch(reason))
        {
            throw new ArgumentException("A failure reason must be a stable lower-case code.", nameof(value));
        }

        return reason;
    }

    private static string? NormalizeEffectiveUrl(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var url = HttpMonitor.NormalizeTarget(value, out _);
        if (url.Length > MaximumEffectiveUrlLength)
        {
            throw new ArgumentException("The effective URL is too long.", nameof(value));
        }

        return url;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReasonPattern();
}
