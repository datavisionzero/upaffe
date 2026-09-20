using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Access;

public sealed class ReadBootstrapState(IBootstrapStore store)
{
    public Task<BootstrapState> ExecuteAsync(CancellationToken cancellationToken) =>
        store.ReadAsync(cancellationToken);
}

/// <summary>Establishes the sole operator and first management credential together.</summary>
public sealed class EstablishOperator(IBootstrapStore store, IPasswordHasher passwords, TimeProvider clock)
{
    public async Task<IssuedCredential> ExecuteAsync(
        string? email, string? password, string? credentialName, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        string? acceptedEmail = null;
        string? acceptedPassword = null;
        string? acceptedName = null;
        try { acceptedEmail = Operator.ValidateEmail(email ?? string.Empty); }
        catch (ArgumentException exception) { errors["email"] = [exception.Message]; }
        try { acceptedPassword = Password.Validate(password); }
        catch (ArgumentException exception) { errors["password"] = [exception.Message]; }
        try { acceptedName = ManagementCredential.ValidateName(credentialName ?? string.Empty); }
        catch (ArgumentException exception) { errors["credential_name"] = [exception.Message]; }
        if (errors.Count > 0) throw Refusal.Validation(errors);

        var now = clock.GetUtcNow();
        var hash = await passwords.HashAsync(acceptedPassword!, cancellationToken);
        var created = Operator.Establish(acceptedEmail!, hash, now);
        return await store.EstablishAsync(created, acceptedName!, now, cancellationToken)
            ?? throw Refusal.AlreadyInitialized();
    }
}

/// <summary>Issues a replacement token through the host-only local command.</summary>
public sealed class RecoverManagementCredential(IBootstrapStore store, TimeProvider clock)
{
    public async Task<IssuedCredential> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        string accepted;
        try { accepted = ManagementCredential.ValidateName(name ?? string.Empty); }
        catch (ArgumentException exception)
        {
            throw Refusal.Validation(new Dictionary<string, string[]> { ["name"] = [exception.Message] });
        }

        return await store.RecoverCredentialAsync(accepted, clock.GetUtcNow(), cancellationToken)
            ?? throw Refusal.NotFound("No active management credential uses that name.");
    }
}
