namespace Upaffe.Domain.Access;

/// <summary>The authenticated origin of a management action.</summary>
public sealed record Identity(Guid OperatorId, AccessPath Path, Guid AccessId);

public enum AccessPath
{
    BrowserSession,
    ManagementCredential,
}
