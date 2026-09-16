using System.Text.RegularExpressions;

namespace Upaffe.Domain.Monitoring;

/// <summary>A persistent definition of a publicly routable HTTP target to observe.</summary>
public sealed partial class HttpMonitor
{
    public const int MaximumKeyLength = 40;
    public const int MaximumNameLength = 100;
    public const int MaximumTargetLength = 2_048;
    public const int MaximumTextFragmentLength = 4_096;
    public const int MaximumInstructionLength = 1_000;
    public const int MaximumRunbookUrlLength = 2_048;
    public const int MinimumIntervalSeconds = 30;
    public const int MaximumIntervalSeconds = 30 * 24 * 60 * 60;
    public const int MinimumTimeoutSeconds = 1;
    public const int MaximumTimeoutSeconds = 60;
    public const int MinimumFailureThreshold = 1;
    public const int MaximumFailureThreshold = 100;

    private HttpMonitor()
    {
    }

    private HttpMonitor(
        Guid projectId,
        string key,
        string name,
        string targetUrl,
        int expectedStatusCode,
        TextCondition textCondition,
        string? textFragment,
        int intervalSeconds,
        int timeoutSeconds,
        int failureThreshold,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("A project is required.", nameof(projectId));
        }

        Id = Guid.NewGuid();
        ProjectId = projectId;
        Key = ValidateKey(key);
        ApplyConfiguration(
            name,
            targetUrl,
            expectedStatusCode,
            textCondition,
            textFragment,
            intervalSeconds,
            timeoutSeconds,
            failureThreshold,
            instruction,
            runbookUrl);
        State = MonitorState.Untested;
        EvaluationGeneration = 1;
        NextSequence = 1;
        Version = 1;
        NextCheckAt = now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TargetUrl { get; private set; } = string.Empty;
    public bool HasTargetQuery { get; private set; }
    public int ExpectedStatusCode { get; private set; }
    public TextCondition TextCondition { get; private set; }
    public string? TextFragment { get; private set; }
    public int IntervalSeconds { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public int FailureThreshold { get; private set; }
    public string? Instruction { get; private set; }
    public string? RunbookUrl { get; private set; }
    public MonitorState State { get; private set; }
    public long EvaluationGeneration { get; private set; }
    public long NextSequence { get; private set; }
    public long LastAppliedSequence { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? NextCheckAt { get; private set; }
    public Guid? LatestResultId { get; private set; }
    public Guid? LatestSuccessId { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PausedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static HttpMonitor Create(
        Guid projectId,
        string key,
        string name,
        string targetUrl,
        int expectedStatusCode,
        TextCondition textCondition,
        string? textFragment,
        int intervalSeconds,
        int timeoutSeconds,
        int failureThreshold,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now) => new(
            projectId,
            key,
            name,
            targetUrl,
            expectedStatusCode,
            textCondition,
            textFragment,
            intervalSeconds,
            timeoutSeconds,
            failureThreshold,
            instruction,
            runbookUrl,
            now);

    public void ChangeConfiguration(
        string name,
        string targetUrl,
        int expectedStatusCode,
        TextCondition textCondition,
        string? textFragment,
        int intervalSeconds,
        int timeoutSeconds,
        int failureThreshold,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now)
    {
        EnsureLive();
        ApplyConfiguration(
            name,
            targetUrl,
            expectedStatusCode,
            textCondition,
            textFragment,
            intervalSeconds,
            timeoutSeconds,
            failureThreshold,
            instruction,
            runbookUrl);
        Changed(now);
    }

    public void Pause(DateTimeOffset now)
    {
        EnsureLive();
        if (State == MonitorState.Paused)
        {
            return;
        }

        State = MonitorState.Paused;
        PausedAt = now;
        NextCheckAt = null;
        Changed(now);
    }

    public void Resume(DateTimeOffset now)
    {
        EnsureLive();
        if (State != MonitorState.Paused)
        {
            return;
        }

        State = MonitorState.Untested;
        PausedAt = null;
        EvaluationGeneration++;
        ConsecutiveFailures = 0;
        NextCheckAt = now;
        Changed(now);
    }

    public void Remove(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        DeletedAt = now;
        NextCheckAt = null;
        Changed(now);
    }

    public void RecordSecretChange(DateTimeOffset now)
    {
        EnsureLive();
        Changed(now);
    }

    public HttpCheck BeginCheck(CheckTrigger trigger, DateTimeOffset scheduledFor, DateTimeOffset startedAt)
    {
        EnsureLive();
        if (State == MonitorState.Paused)
        {
            throw new InvalidOperationException("A paused monitor cannot begin a check.");
        }

        var check = HttpCheck.Begin(Id, EvaluationGeneration, NextSequence, trigger, scheduledFor, startedAt);
        NextSequence++;
        if (trigger == CheckTrigger.Scheduled)
        {
            NextCheckAt = scheduledFor.AddSeconds(IntervalSeconds);
        }

        return check;
    }

    public bool ApplyResult(HttpCheck check, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(check);
        EnsureLive();
        if (State == MonitorState.Paused
            || !check.IsCompleted
            || check.MonitorId != Id
            || check.EvaluationGeneration != EvaluationGeneration
            || check.Sequence <= LastAppliedSequence)
        {
            return false;
        }

        LatestResultId = check.Id;
        LastAppliedSequence = check.Sequence;
        if (check.Outcome == CheckOutcome.Success)
        {
            State = MonitorState.Healthy;
            LatestSuccessId = check.Id;
            ConsecutiveFailures = 0;
        }
        else
        {
            State = MonitorState.Failing;
            ConsecutiveFailures++;
        }

        UpdatedAt = now;
        return true;
    }

    internal static string NormalizeTarget(string value, out string? query)
    {
        var target = (value ?? string.Empty).Trim();
        if (target.Length is < 1 or > MaximumTargetLength
            || !Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "A target must be an absolute HTTP or HTTPS URL without user information or a fragment.",
                nameof(value));
        }

        query = string.IsNullOrEmpty(uri.Query) ? null : uri.Query;
        return uri.GetLeftPart(UriPartial.Path);
    }

    public static string ValidateKey(string value)
    {
        var key = (value ?? string.Empty).Trim();
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException(
                "A monitor key must be 2-40 lower-case letters, digits, or hyphens and start with a letter.",
                nameof(value));
        }

        return key;
    }

    private void ApplyConfiguration(
        string name,
        string targetUrl,
        int expectedStatusCode,
        TextCondition textCondition,
        string? textFragment,
        int intervalSeconds,
        int timeoutSeconds,
        int failureThreshold,
        string? instruction,
        string? runbookUrl)
    {
        var acceptedName = (name ?? string.Empty).Trim();
        if (acceptedName.Length is < 1 or > MaximumNameLength)
        {
            throw new ArgumentException("A monitor name must be 1-100 characters.", nameof(name));
        }

        var acceptedTarget = NormalizeTarget(targetUrl, out var query);
        if (expectedStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedStatusCode));
        }

        var acceptedFragment = string.IsNullOrEmpty(textFragment) ? null : textFragment;
        if (!Enum.IsDefined(textCondition)
            || (textCondition == TextCondition.None && acceptedFragment is not null)
            || (textCondition != TextCondition.None
                && (acceptedFragment is null || acceptedFragment.EnumerateRunes().Count() > MaximumTextFragmentLength)))
        {
            throw new ArgumentException("A required or forbidden text condition needs a 1-4096 character fragment.", nameof(textFragment));
        }

        if (intervalSeconds is < MinimumIntervalSeconds or > MaximumIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        }

        if (timeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds
            || timeoutSeconds > intervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        if (failureThreshold is < MinimumFailureThreshold or > MaximumFailureThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));
        }

        var acceptedInstruction = NormalizeOptional(instruction, MaximumInstructionLength, nameof(instruction));
        var acceptedRunbook = NormalizeRunbook(runbookUrl);

        Name = acceptedName;
        TargetUrl = acceptedTarget;
        HasTargetQuery = query is not null;
        ExpectedStatusCode = expectedStatusCode;
        TextCondition = textCondition;
        TextFragment = acceptedFragment;
        IntervalSeconds = intervalSeconds;
        TimeoutSeconds = timeoutSeconds;
        FailureThreshold = failureThreshold;
        Instruction = acceptedInstruction;
        RunbookUrl = acceptedRunbook;
    }

    private static string? NormalizeOptional(string? value, int maximumLength, string parameter)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value may contain at most {maximumLength} characters.", parameter);
        }

        return normalized;
    }

    private static string? NormalizeRunbook(string? value)
    {
        var runbook = NormalizeOptional(value, MaximumRunbookUrlLength, nameof(value));
        if (runbook is null)
        {
            return null;
        }

        if (!Uri.TryCreate(runbook, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("A runbook must be an absolute HTTP or HTTPS URL without user information.", nameof(value));
        }

        return uri.AbsoluteUri;
    }

    private void EnsureLive()
    {
        if (DeletedAt is not null)
        {
            throw new InvalidOperationException("A removed monitor cannot change.");
        }
    }

    private void Changed(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
