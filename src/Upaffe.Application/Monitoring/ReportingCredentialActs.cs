using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Monitoring;

public sealed class ReadReportingCredential(IReportingCredentialStore store)
{
    public async Task<ReportingCredentialMetadata> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, CancellationToken cancellationToken)
    {
        _ = identity;
        return await store.GetAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey), cancellationToken)
            ?? throw Refusal.NotFound("No reporting credential exists for that push monitor.");
    }
}

public sealed class IssueReportingCredential(IReportingCredentialStore store, TimeProvider clock)
{
    public async Task<IssuedReportingCredential> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await store.IssueAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey), clock.GetUtcNow(), cancellationToken);
        return result.Outcome switch
        {
            ReportingCredentialMutation.Changed when result.Issued is not null => result.Issued,
            ReportingCredentialMutation.Missing => throw Refusal.NotFound("No such push monitor."),
            ReportingCredentialMutation.Conflict => throw Refusal.Conflict("The push monitor already has a reporting credential."),
            _ => throw new InvalidOperationException("Invalid reporting credential issue result."),
        };
    }
}

public sealed class RotateReportingCredential(IReportingCredentialStore store, TimeProvider clock)
{
    public async Task<IssuedReportingCredential> ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await store.RotateAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey), clock.GetUtcNow(), cancellationToken);
        return result.Outcome switch
        {
            ReportingCredentialMutation.Changed when result.Issued is not null => result.Issued,
            ReportingCredentialMutation.Missing => throw Refusal.NotFound("No reporting credential exists for that push monitor."),
            ReportingCredentialMutation.Revoked => throw Refusal.Conflict("A revoked reporting credential cannot be rotated."),
            _ => throw new InvalidOperationException("Invalid reporting credential rotation result."),
        };
    }
}

public sealed class RevokeReportingCredential(IReportingCredentialStore store, TimeProvider clock)
{
    public async Task ExecuteAsync(Identity identity, string? projectKey, string? monitorKey, CancellationToken cancellationToken)
    {
        _ = identity;
        if (await store.RevokeAsync(PushMonitorValidation.ProjectKey(projectKey), PushMonitorValidation.MonitorKey(monitorKey), clock.GetUtcNow(), cancellationToken)
            == ReportingCredentialMutation.Missing) throw Refusal.NotFound("No reporting credential exists for that push monitor.");
    }
}
