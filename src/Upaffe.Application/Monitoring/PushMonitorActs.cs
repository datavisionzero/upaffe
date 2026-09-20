using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Monitoring;

public sealed record CreatedPushMonitor(PushMonitorSnapshot Monitor, bool Created);

public sealed class CreatePushMonitor(IPushMonitorStore monitors, TimeProvider clock)
{
    public async Task<CreatedPushMonitor> ExecuteAsync(Identity identity, string? projectKey, string? key, string? name,
        string? mode, int? intervalSeconds, int? toleranceSeconds, string? instruction, string? runbookUrl,
        CancellationToken cancellationToken, string? purpose = null)
    {
        _ = identity;
        var now = clock.GetUtcNow();
        var definition = PushMonitorValidation.Definition(key, name, mode, intervalSeconds, toleranceSeconds, instruction, runbookUrl, now, purpose);
        var result = await monitors.CreateAsync(PushMonitorValidation.ProjectKey(projectKey), definition, now, cancellationToken);
        return result.Outcome switch
        {
            PushMonitorMutation.Changed when result.Monitor is not null => new(result.Monitor, true),
            PushMonitorMutation.Unchanged when result.Monitor is not null => new(result.Monitor, false),
            PushMonitorMutation.ProjectMissing => throw Refusal.NotFound("No such project."),
            PushMonitorMutation.ProjectDeleted => throw Refusal.Conflict("A deleted project cannot receive monitors."),
            PushMonitorMutation.Conflict => throw Refusal.Conflict("A monitor already uses that key with different facts."),
            _ => throw PushMonitorValidation.InvalidStoreResult("create"),
        };
    }
}

public sealed class ReadPushMonitor(IPushMonitorStore monitors)
{
    public async Task<PushMonitorSnapshot> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, CancellationToken cancellationToken)
    {
        _ = identity;
        return await monitors.GetAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey), cancellationToken)
            ?? throw Refusal.NotFound("No such push monitor.");
    }
}

public sealed class ListPushMonitors(IPushMonitorStore monitors)
{
    public async Task<IReadOnlyList<PushMonitorSnapshot>> ExecuteAsync(Identity identity, string? projectKey, CancellationToken cancellationToken)
    {
        _ = identity;
        return await monitors.ListAsync(PushMonitorValidation.ProjectKey(projectKey), cancellationToken)
            ?? throw Refusal.NotFound("No such project.");
    }
}

public sealed class UpdatePushMonitor(IPushMonitorStore monitors, TimeProvider clock)
{
    public async Task<PushMonitorSnapshot> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, string? name,
        int? intervalSeconds, int? toleranceSeconds, string? instruction, string? runbookUrl, long? version,
        CancellationToken cancellationToken, string? purpose = null)
    {
        _ = identity;
        var change = PushMonitorValidation.Change(name, intervalSeconds, toleranceSeconds, instruction, runbookUrl, clock.GetUtcNow(), purpose);
        return PushMonitorValidation.Changed(await monitors.UpdateAsync(PushMonitorValidation.ProjectKey(projectKey),
            PushMonitorValidation.MonitorKey(monitorKey), change, PushMonitorValidation.Version(version), clock.GetUtcNow(), cancellationToken), "update");
    }
}

public sealed class PausePushMonitor(IPushMonitorStore monitors, TimeProvider clock)
{
    public async Task<PushMonitorSnapshot> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, long? version, CancellationToken cancellationToken)
    {
        _ = identity;
        return PushMonitorValidation.Changed(await monitors.PauseAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey),
            PushMonitorValidation.Version(version), clock.GetUtcNow(), cancellationToken), "pause", true);
    }
}

public sealed class ResumePushMonitor(IPushMonitorStore monitors, TimeProvider clock)
{
    public async Task<PushMonitorSnapshot> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, long? version, CancellationToken cancellationToken)
    {
        _ = identity;
        return PushMonitorValidation.Changed(await monitors.ResumeAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey),
            PushMonitorValidation.Version(version), clock.GetUtcNow(), cancellationToken), "resume", true);
    }
}

public sealed class RemovePushMonitor(IPushMonitorStore monitors, TimeProvider clock)
{
    public async Task<PushMonitorSnapshot> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, long? version, CancellationToken cancellationToken)
    {
        _ = identity;
        return PushMonitorValidation.Changed(await monitors.RemoveAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey),
            PushMonitorValidation.Version(version), clock.GetUtcNow(), cancellationToken), "remove", true);
    }
}

internal static class PushMonitorValidation
{
    public static string ProjectKey(string? value) => Validate("project_key", () => Project.ValidateKey(value ?? string.Empty));
    public static string MonitorKey(string? value) => Validate("monitor_key", () => PushMonitor.ValidateKey(value ?? string.Empty));

    public static long Version(long? value) => value is > 0 ? value.Value : throw Refusal.Validation(new Dictionary<string, string[]> { ["version"] = ["A positive push monitor version is required."] });

    public static PushMonitorDefinition Definition(string? key, string? name, string? mode, int? interval, int? tolerance, string? instruction, string? runbook, DateTimeOffset now, string? purpose = null)
    {
        var parsedMode = mode switch
        {
            "job_completion" => PushMonitorMode.JobCompletion,
            "state_report" => PushMonitorMode.StateReport,
            _ => throw Refusal.Validation(new Dictionary<string, string[]> { ["mode"] = ["Mode must be job_completion or state_report."] }),
        };
        var accepted = Validate("monitor", () => PushMonitor.Create(Guid.NewGuid(), key ?? string.Empty, name ?? string.Empty,
            parsedMode, interval ?? 0, tolerance ?? -1, instruction, runbook, now, purpose));
        return new(accepted.Key, accepted.Name, accepted.Mode, accepted.IntervalSeconds, accepted.ToleranceSeconds, accepted.Instruction, accepted.RunbookUrl, accepted.Purpose);
    }

    public static PushMonitorChange Change(string? name, int? interval, int? tolerance, string? instruction, string? runbook, DateTimeOffset now, string? purpose = null)
    {
        var value = Definition("validation-only", name, "job_completion", interval, tolerance, instruction, runbook, now, purpose);
        return new(value.Name, value.IntervalSeconds, value.ToleranceSeconds, value.Instruction, value.RunbookUrl, value.Purpose);
    }

    public static PushMonitorSnapshot Changed(PushMonitorMutationResult result, string operation, bool allowUnchanged = false) => result.Outcome switch
    {
        PushMonitorMutation.Changed when result.Monitor is not null => result.Monitor,
        PushMonitorMutation.Unchanged when allowUnchanged && result.Monitor is not null => result.Monitor,
        PushMonitorMutation.Missing or PushMonitorMutation.ProjectMissing => throw Refusal.NotFound("No such push monitor."),
        PushMonitorMutation.ProjectDeleted or PushMonitorMutation.Deleted => throw Refusal.Conflict("A removed push monitor cannot change."),
        PushMonitorMutation.VersionConflict => throw Refusal.Conflict("The push monitor changed since it was read."),
        _ => throw InvalidStoreResult(operation),
    };

    public static InvalidOperationException InvalidStoreResult(string operation) => new($"The push monitor store returned an invalid {operation} result.");

    private static T Validate<T>(string field, Func<T> operation)
    {
        try { return operation(); }
        catch (ArgumentException exception) { throw Refusal.Validation(new Dictionary<string, string[]> { [field] = [exception.Message] }); }
    }
}
