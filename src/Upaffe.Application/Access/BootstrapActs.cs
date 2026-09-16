using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Access;

public sealed class ArmBootstrap(IBootstrapStore store, TimeProvider clock)
{
    public Task ExecuteAsync(BootstrapSettings settings, CancellationToken cancellationToken)
    {
        var secret = settings.ValidatedSecret();
        return store.ArmAsync(
            secret is null ? null : SecretValue.Hash(secret),
            clock.GetUtcNow(),
            cancellationToken);
    }
}

public sealed class ReadBootstrapState(IBootstrapStore store, TimeProvider clock)
{
    public Task<BootstrapState> ExecuteAsync(CancellationToken cancellationToken) =>
        store.ReadAsync(clock.GetUtcNow(), cancellationToken);
}

public sealed class EstablishOperator(
    IBootstrapStore store,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    public async Task ExecuteAsync(
        string? proof,
        string? email,
        string? password,
        CancellationToken cancellationToken)
    {
        var proofHash = HashProof(proof);
        var now = clock.GetUtcNow();
        ThrowUnlessAccepted(await store.CheckAsync(proofHash, now, cancellationToken));

        var errors = new Dictionary<string, string[]>();
        string? acceptedEmail = null;
        string? acceptedPassword = null;
        try
        {
            acceptedEmail = Operator.ValidateEmail(email ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            errors["email"] = [exception.Message];
        }

        try
        {
            acceptedPassword = Password.Validate(password);
        }
        catch (ArgumentException exception)
        {
            errors["password"] = [exception.Message];
        }

        if (errors.Count > 0)
        {
            throw Refusal.Validation(errors);
        }

        var passwordHash = await passwords.HashAsync(acceptedPassword!, cancellationToken);
        var created = Operator.Establish(acceptedEmail!, passwordHash, now);
        ThrowUnlessAccepted(await store.EstablishAsync(
            proofHash, created, now, cancellationToken));
    }

    private static byte[] HashProof(string? proof) =>
        proof is { Length: >= BootstrapSettings.MinimumLength and <= BootstrapSettings.MaximumLength }
            ? SecretValue.Hash(proof)
            : SecretValue.Hash("a-deliberately-invalid-bootstrap-proof");

    private static void ThrowUnlessAccepted(BootstrapProof proof)
    {
        if (proof == BootstrapProof.Rejected)
        {
            throw Refusal.BootstrapRejected();
        }

        if (proof == BootstrapProof.Closed)
        {
            throw Refusal.BootstrapClosed();
        }
    }
}
