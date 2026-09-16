using System.Text;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Monitoring;

public sealed record CreatedHttpMonitor(HttpMonitorSnapshot Monitor, bool Created);

public sealed class CreateHttpMonitor(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<CreatedHttpMonitor> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? key,
        string? name,
        string? targetUrl,
        int? expectedStatusCode,
        string? textCondition,
        string? textFragment,
        int? intervalSeconds,
        int? timeoutSeconds,
        int? failureThreshold,
        string? instruction,
        string? runbookUrl,
        IReadOnlyList<HttpHeaderValue>? headers,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var now = clock.GetUtcNow();
        var definition = HttpMonitorValidation.Definition(
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
        var result = await monitors.CreateAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            definition,
            HttpMonitorValidation.Headers(headers),
            now,
            cancellationToken);
        return result.Outcome switch
        {
            HttpMonitorMutation.Changed when result.Monitor is not null => new(result.Monitor, true),
            HttpMonitorMutation.Unchanged when result.Monitor is not null => new(result.Monitor, false),
            HttpMonitorMutation.ProjectMissing => throw Refusal.NotFound("No such project."),
            HttpMonitorMutation.ProjectDeleted => throw Refusal.Conflict("A deleted project cannot receive monitors."),
            HttpMonitorMutation.Conflict => throw Refusal.Conflict("An HTTP monitor already uses that key with different facts."),
            _ => throw HttpMonitorValidation.InvalidStoreResult("create"),
        };
    }
}

public sealed class ReadHttpMonitor(IHttpMonitorStore monitors)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return await monitors.GetAsync(
                HttpMonitorValidation.ProjectKey(projectKey),
                HttpMonitorValidation.MonitorKey(monitorKey),
                cancellationToken)
            ?? throw Refusal.NotFound("No such HTTP monitor.");
    }
}

public sealed class ListHttpMonitors(IHttpMonitorStore monitors)
{
    public async Task<IReadOnlyList<HttpMonitorSnapshot>> ExecuteAsync(
        Identity identity,
        string? projectKey,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return await monitors.ListAsync(
                HttpMonitorValidation.ProjectKey(projectKey),
                cancellationToken)
            ?? throw Refusal.NotFound("No such project.");
    }
}

public sealed class UpdateHttpMonitor(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        string? name,
        string? targetUrl,
        int? expectedStatusCode,
        string? textCondition,
        string? textFragment,
        int? intervalSeconds,
        int? timeoutSeconds,
        int? failureThreshold,
        string? instruction,
        string? runbookUrl,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var now = clock.GetUtcNow();
        var change = HttpMonitorValidation.Change(
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
        var result = await monitors.UpdateAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            change,
            HttpMonitorValidation.Version(version),
            now,
            cancellationToken);
        return HttpMonitorValidation.Changed(result, "update");
    }
}

public sealed class PauseHttpMonitor(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await monitors.PauseAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            HttpMonitorValidation.Version(version),
            clock.GetUtcNow(),
            cancellationToken);
        return HttpMonitorValidation.Changed(result, "pause", allowUnchanged: true);
    }
}

public sealed class ResumeHttpMonitor(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await monitors.ResumeAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            HttpMonitorValidation.Version(version),
            clock.GetUtcNow(),
            cancellationToken);
        return HttpMonitorValidation.Changed(result, "resume", allowUnchanged: true);
    }
}

public sealed class RemoveHttpMonitor(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await monitors.RemoveAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            HttpMonitorValidation.Version(version),
            clock.GetUtcNow(),
            cancellationToken);
        return HttpMonitorValidation.Changed(result, "remove", allowUnchanged: true);
    }
}

public sealed class SetHttpMonitorHeader(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        string? name,
        string? value,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var header = HttpMonitorValidation.Headers([new(name ?? string.Empty, value ?? string.Empty)]).Single();
        var result = await monitors.SetHeaderAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            header.Name,
            header.Value,
            HttpMonitorValidation.Version(version),
            clock.GetUtcNow(),
            cancellationToken);
        return HttpMonitorValidation.Changed(result, "set header", allowUnchanged: true);
    }
}

public sealed class RemoveHttpMonitorHeader(IHttpMonitorStore monitors, TimeProvider clock)
{
    public async Task<HttpMonitorSnapshot> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        string? name,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var acceptedName = HttpMonitorValidation.HeaderName(name);
        var result = await monitors.RemoveHeaderAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            acceptedName,
            HttpMonitorValidation.Version(version),
            clock.GetUtcNow(),
            cancellationToken);
        return HttpMonitorValidation.Changed(result, "remove header", allowUnchanged: true);
    }
}

public sealed class TestHttpMonitor(
    IHttpMonitorStore monitors,
    IHttpCheckExecutor executor,
    TimeProvider clock)
{
    public async Task<CompletedHttpTest> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var started = await monitors.StartTestAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            clock.GetUtcNow(),
            cancellationToken);
        if (started.Outcome != HttpMonitorMutation.Changed
            || started.CheckId is null
            || started.Request is null)
        {
            throw started.Outcome switch
            {
                HttpMonitorMutation.Missing or HttpMonitorMutation.ProjectMissing =>
                    Refusal.NotFound("No such HTTP monitor."),
                HttpMonitorMutation.ProjectDeleted or HttpMonitorMutation.Deleted =>
                    Refusal.Conflict("A removed HTTP monitor cannot be tested."),
                HttpMonitorMutation.Paused =>
                    Refusal.Conflict("A paused HTTP monitor cannot be tested."),
                HttpMonitorMutation.VersionConflict =>
                    Refusal.Conflict("The HTTP monitor changed while the test was starting."),
                _ => HttpMonitorValidation.InvalidStoreResult("start test"),
            };
        }

        var result = await executor.ExecuteAsync(started.Request, cancellationToken);
        return await monitors.CompleteTestAsync(
            started.CheckId.Value,
            result,
            clock.GetUtcNow(),
            cancellationToken);
    }
}

internal static class HttpMonitorValidation
{
    public static string ProjectKey(string? value) => Validate(
        "project_key",
        () => Project.ValidateKey(value ?? string.Empty));

    public static string MonitorKey(string? value) => Validate(
        "monitor_key",
        () => HttpMonitor.ValidateKey(value ?? string.Empty));

    public static long Version(long? value)
    {
        if (value is null or <= 0)
        {
            throw Refusal.Validation(new Dictionary<string, string[]>
            {
                ["version"] = ["A positive HTTP monitor version is required."],
            });
        }

        return value.Value;
    }

    public static HttpMonitorDefinition Definition(
        string? key,
        string? name,
        string? targetUrl,
        int? expectedStatusCode,
        string? textCondition,
        string? textFragment,
        int? intervalSeconds,
        int? timeoutSeconds,
        int? failureThreshold,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now)
    {
        var condition = Condition(textCondition);
        var accepted = Validate(
            "monitor",
            () => HttpMonitor.Create(
                Guid.NewGuid(),
                key ?? string.Empty,
                name ?? string.Empty,
                targetUrl ?? string.Empty,
                expectedStatusCode ?? 0,
                condition,
                textFragment,
                intervalSeconds ?? 0,
                timeoutSeconds ?? 0,
                failureThreshold ?? 0,
                instruction,
                runbookUrl,
                now));
        return new(
            accepted.Key,
            accepted.Name,
            targetUrl!.Trim(),
            accepted.ExpectedStatusCode,
            accepted.TextCondition,
            accepted.TextFragment,
            accepted.IntervalSeconds,
            accepted.TimeoutSeconds,
            accepted.FailureThreshold,
            accepted.Instruction,
            accepted.RunbookUrl);
    }

    public static HttpMonitorChange Change(
        string? name,
        string? targetUrl,
        int? expectedStatusCode,
        string? textCondition,
        string? textFragment,
        int? intervalSeconds,
        int? timeoutSeconds,
        int? failureThreshold,
        string? instruction,
        string? runbookUrl,
        DateTimeOffset now)
    {
        var validated = Definition(
            "validation-only",
            name,
            targetUrl ?? "https://validation.example/",
            expectedStatusCode,
            textCondition,
            textFragment,
            intervalSeconds,
            timeoutSeconds,
            failureThreshold,
            instruction,
            runbookUrl,
            now);
        return new(
            validated.Name,
            targetUrl?.Trim(),
            validated.ExpectedStatusCode,
            validated.TextCondition,
            validated.TextFragment,
            validated.IntervalSeconds,
            validated.TimeoutSeconds,
            validated.FailureThreshold,
            validated.Instruction,
            validated.RunbookUrl);
    }

    public static IReadOnlyList<HttpHeaderValue> Headers(IReadOnlyList<HttpHeaderValue>? values)
    {
        values ??= [];
        if (values.Count > 32)
        {
            throw ValidationFailure("headers", "At most 32 HTTP headers may be configured.");
        }

        var result = new List<HttpHeaderValue>(values.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        foreach (var value in values)
        {
            var accepted = Validate(
                "headers",
                () => HttpMonitorHeader.Create(Guid.NewGuid(), value.Name, value.Value, DateTimeOffset.UnixEpoch));
            if (!names.Add(accepted.Header.Name))
            {
                throw ValidationFailure("headers", "HTTP header names must be unique.");
            }

            totalBytes += Encoding.ASCII.GetByteCount(accepted.Header.Name) + accepted.Secret.ValueUtf8.Length;
            result.Add(new(accepted.Header.Name, value.Value));
        }

        if (totalBytes > 16 * 1_024)
        {
            throw ValidationFailure("headers", "HTTP headers exceed their total 16 KiB limit.");
        }

        return result;
    }

    public static string HeaderName(string? value) => Validate(
        "header_name",
        () => HttpMonitorHeader.ValidateName(value ?? string.Empty));

    public static HttpMonitorSnapshot Changed(
        HttpMonitorMutationResult result,
        string operation,
        bool allowUnchanged = false) => result.Outcome switch
    {
        HttpMonitorMutation.Changed when result.Monitor is not null => result.Monitor,
        HttpMonitorMutation.Unchanged when allowUnchanged && result.Monitor is not null => result.Monitor,
        HttpMonitorMutation.Missing or HttpMonitorMutation.ProjectMissing =>
            throw Refusal.NotFound("No such HTTP monitor."),
        HttpMonitorMutation.ProjectDeleted or HttpMonitorMutation.Deleted =>
            throw Refusal.Conflict("A removed HTTP monitor cannot change."),
        HttpMonitorMutation.Paused => throw Refusal.Conflict("A paused HTTP monitor cannot perform that operation."),
        HttpMonitorMutation.VersionConflict =>
            throw Refusal.Conflict("The HTTP monitor changed after the supplied version was read."),
        HttpMonitorMutation.Conflict => throw Refusal.Conflict("The HTTP monitor change conflicts with stored facts."),
        _ => throw InvalidStoreResult(operation),
    };

    public static InvalidOperationException InvalidStoreResult(string operation) =>
        new($"The HTTP monitor store returned an invalid {operation} outcome.");

    private static TextCondition Condition(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => TextCondition.None,
        "required" => TextCondition.Required,
        "forbidden" => TextCondition.Forbidden,
        _ => throw ValidationFailure(
            "text_condition",
            "Text condition must be none, required, or forbidden."),
    };

    private static T Validate<T>(string field, Func<T> accept)
    {
        try
        {
            return accept();
        }
        catch (ArgumentException exception)
        {
            throw ValidationFailure(field, exception.Message);
        }
    }

    private static Refusal ValidationFailure(string field, string message) =>
        Refusal.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
