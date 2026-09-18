using System.Text.RegularExpressions;

namespace Upaffe.Domain.Monitoring;

/// <summary>A persistent definition whose observations are submitted by an external sender.</summary>
public sealed partial class PushMonitor
{
    public const int MaximumKeyLength = 40;
    public const int MaximumNameLength = 100;
    public const int MaximumInstructionLength = 1_000;
    public const int MaximumRunbookUrlLength = 2_048;
    public const int MinimumIntervalSeconds = 30;
    public const int MaximumIntervalSeconds = 365 * 24 * 60 * 60;
    public const int MaximumToleranceSeconds = 30 * 24 * 60 * 60;

    private PushMonitor()
    {
    }

    private PushMonitor(
        Guid projectId,
        string key,
        string name,
        PushMonitorMode mode,
        int intervalSeconds,
        int toleranceSeconds,
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
        Mode = mode;
        ApplyConfiguration(name, intervalSeconds, toleranceSeconds, instruction, runbookUrl);
        State = MonitorState.Untested;
        EvaluationGeneration = 1;
        NextSequence = 1;
        Version = 1;
        NextDeadlineAt = now.AddSeconds(intervalSeconds + toleranceSeconds);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PushMonitorMode Mode { get; private set; }
    public int IntervalSeconds { get; private set; }
    public int ToleranceSeconds { get; private set; }
    public string? Instruction { get; private set; }
    public string? RunbookUrl { get; private set; }
    public MonitorState State { get; private set; }
    public long EvaluationGeneration { get; private set; }
    public long NextSequence { get; private set; }
    public long LastAppliedSequence { get; private set; }
    public DateTimeOffset? LastAppliedObservedAt { get; private set; }
    public DateTimeOffset? LastReceivedAt { get; private set; }
    public DateTimeOffset? NextDeadlineAt { get; private set; }
    public Guid? LatestReportId { get; private set; }
    public Guid? LatestSuccessId { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PausedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public static PushMonitor Create(
        Guid projectId,
        string key,
        string name,
        PushMonitorMode mode,
        int intervalSeconds,
        int toleranceSeconds,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now) => new(
            projectId,
            key,
            name,
            mode,
            intervalSeconds,
            toleranceSeconds,
            instruction,
            runbookUrl,
            now);

    public void ChangeConfiguration(
        string name,
        int intervalSeconds,
        int toleranceSeconds,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now)
    {
        EnsureLive();
        ApplyConfiguration(name, intervalSeconds, toleranceSeconds, instruction, runbookUrl);
        if (State != MonitorState.Paused)
        {
            NextDeadlineAt = now.AddSeconds(intervalSeconds + toleranceSeconds);
        }

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
        NextDeadlineAt = null;
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
        LastAppliedObservedAt = null;
        NextDeadlineAt = now.AddSeconds(IntervalSeconds + ToleranceSeconds);
        Changed(now);
    }

    public void Remove(DateTimeOffset now)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        DeletedAt = now;
        NextDeadlineAt = null;
        Changed(now);
    }

    public PushReport Receive(
        Guid reportId,
        DateTimeOffset observedAt,
        DateTimeOffset receivedAt,
        ReportOutcome outcome,
        string? diagnosticReason)
    {
        EnsureLive();
        if (State == MonitorState.Paused)
        {
            throw new InvalidOperationException("A paused monitor cannot receive a report.");
        }

        var applicable = LastAppliedObservedAt is null || observedAt > LastAppliedObservedAt;
        var report = PushReport.Receive(
            Id,
            reportId,
            EvaluationGeneration,
            NextSequence,
            observedAt,
            receivedAt,
            outcome,
            diagnosticReason,
            applicable);
        NextSequence++;
        LastReceivedAt = receivedAt;
        LatestReportId = report.Id;
        if (applicable)
        {
            LastAppliedSequence = report.Sequence;
            LastAppliedObservedAt = observedAt;
        }

        UpdatedAt = receivedAt;
        return report;
    }

    public PushReport MissDeadline(DateTimeOffset processedAt)
    {
        EnsureLive();
        if (State == MonitorState.Paused || NextDeadlineAt is null || processedAt < NextDeadlineAt)
        {
            throw new InvalidOperationException("The monitor has no crossed reporting deadline.");
        }

        var report = PushReport.Missing(
            Id,
            EvaluationGeneration,
            NextSequence,
            NextDeadlineAt.Value,
            processedAt);
        NextSequence++;
        LatestReportId = report.Id;
        LastAppliedSequence = report.Sequence;
        LastAppliedObservedAt = report.ObservedAt;
        UpdatedAt = processedAt;
        return report;
    }

    public void RecordCredentialChange(DateTimeOffset now)
    {
        EnsureLive();
        Changed(now);
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
        int intervalSeconds,
        int toleranceSeconds,
        string? instruction,
        string? runbookUrl)
    {
        var acceptedName = (name ?? string.Empty).Trim();
        if (acceptedName.Length is < 1 or > MaximumNameLength)
        {
            throw new ArgumentException("A monitor name must be 1-100 characters.", nameof(name));
        }

        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }

        if (intervalSeconds is < MinimumIntervalSeconds or > MaximumIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        }

        if (toleranceSeconds is < 0 or > MaximumToleranceSeconds || toleranceSeconds > intervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(toleranceSeconds));
        }

        Name = acceptedName;
        IntervalSeconds = intervalSeconds;
        ToleranceSeconds = toleranceSeconds;
        Instruction = NormalizeOptional(instruction, MaximumInstructionLength, nameof(instruction));
        RunbookUrl = NormalizeRunbook(runbookUrl);
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
        var normalized = NormalizeOptional(value, MaximumRunbookUrlLength, nameof(value));
        if (normalized is null)
        {
            return null;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A runbook must be an absolute HTTP or HTTPS URL.", nameof(value));
        }

        return normalized;
    }

    private void Changed(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private void EnsureLive()
    {
        if (DeletedAt is not null)
        {
            throw new InvalidOperationException("A removed monitor cannot change.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
