namespace Upaffe.Application.Ports;

public sealed record ReportingCredentialMetadata(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset? RotatedAt, DateTimeOffset? RevokedAt);
public sealed record IssuedReportingCredential(ReportingCredentialMetadata Credential, string Token, DateTimeOffset? PreviousValidUntil = null);

public enum ReportingCredentialMutation { Changed, Missing, Conflict, Revoked }
public sealed record ReportingCredentialMutationResult(ReportingCredentialMutation Outcome, IssuedReportingCredential? Issued = null);

public interface IReportingCredentialStore
{
    Task<ReportingCredentialMetadata?> GetAsync(string projectKey, string monitorKey, CancellationToken cancellationToken);
    Task<ReportingCredentialMutationResult> IssueAsync(string projectKey, string monitorKey, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ReportingCredentialMutationResult> RotateAsync(string projectKey, string monitorKey, DateTimeOffset now, CancellationToken cancellationToken);
    Task<ReportingCredentialMutation> RevokeAsync(string projectKey, string monitorKey, DateTimeOffset now, CancellationToken cancellationToken);
}
