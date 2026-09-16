using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Access;

public sealed class CreateManagementCredential(
    IManagementCredentialStore credentials,
    TimeProvider clock)
{
    public async Task<IssuedCredential> ExecuteAsync(
        Identity identity,
        string? name,
        CancellationToken cancellationToken)
    {
        string accepted;
        try
        {
            accepted = ManagementCredential.ValidateName(name ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            throw Refusal.Validation(new Dictionary<string, string[]> { ["name"] = [exception.Message] });
        }

        return Issued(await credentials.CreateAsync(
            identity.OperatorId,
            accepted,
            clock.GetUtcNow(),
            cancellationToken));
    }

    private static IssuedCredential Issued(CredentialMutationResult result) => result.Outcome switch
    {
        CredentialMutation.Changed when result.Issued is not null => result.Issued,
        CredentialMutation.Conflict => throw Refusal.Conflict("A management credential already uses that name."),
        _ => throw new InvalidOperationException("The credential store returned an invalid create outcome."),
    };
}

public sealed class ListManagementCredentials(IManagementCredentialStore credentials)
{
    public Task<IReadOnlyList<CredentialMetadata>> ExecuteAsync(
        Identity identity,
        CancellationToken cancellationToken) =>
        credentials.ListAsync(identity.OperatorId, cancellationToken);
}

public sealed class RotateManagementCredential(
    IManagementCredentialStore credentials,
    TimeProvider clock)
{
    public async Task<IssuedCredential> ExecuteAsync(
        Identity identity,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var result = await credentials.RotateAsync(
            credentialId,
            identity.OperatorId,
            clock.GetUtcNow(),
            cancellationToken);
        return result.Outcome switch
        {
            CredentialMutation.Changed when result.Issued is not null => result.Issued,
            CredentialMutation.Missing => throw Refusal.NotFound("No such management credential."),
            CredentialMutation.Revoked => throw Refusal.Conflict("A revoked credential cannot be rotated."),
            _ => throw new InvalidOperationException("The credential store returned an invalid rotate outcome."),
        };
    }
}

public sealed class RevokeManagementCredential(
    IManagementCredentialStore credentials,
    TimeProvider clock)
{
    public async Task ExecuteAsync(
        Identity identity,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var result = await credentials.RevokeAsync(
            credentialId,
            identity.OperatorId,
            clock.GetUtcNow(),
            cancellationToken);
        if (result == CredentialMutation.Missing)
        {
            throw Refusal.NotFound("No such management credential.");
        }
    }
}
