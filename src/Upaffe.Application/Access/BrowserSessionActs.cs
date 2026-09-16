using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Access;

public sealed record IssuedBrowserSession(BrowserSession Session, string Secret);

/// <summary>Checks the sole operator and always begins a new browser session.</summary>
public sealed class SignIn(
    IBrowserSessionStore sessions,
    IPasswordHasher passwords,
    TimeProvider clock)
{
    private const string NobodysHash =
        "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$"
        + "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public async Task<IssuedBrowserSession> ExecuteAsync(
        string? email,
        string? password,
        string? description,
        CancellationToken cancellationToken)
    {
        var @operator = await sessions.ReadOperatorAsync(cancellationToken);
        var addressMatches = @operator is not null && Matches(email, @operator.NormalizedEmail);
        var presented = password is { Length: <= Password.MaximumLength }
            ? password
            : "a-deliberately-invalid-password";
        var passwordMatches = await passwords.VerifyAsync(
            @operator?.PasswordHash ?? NobodysHash,
            presented,
            cancellationToken);

        if (@operator is null || !addressMatches || !passwordMatches)
        {
            throw Refusal.SignInRejected();
        }

        var begun = BrowserSession.Begin(@operator.Id, description, clock.GetUtcNow());
        await sessions.AddAsync(begun.Session, cancellationToken);
        return new(begun.Session, begun.Secret);
    }

    private static bool Matches(string? email, string normalizedEmail)
    {
        try
        {
            return Operator.NormalizeEmail(email ?? string.Empty) == normalizedEmail;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class SignOut(IBrowserSessionStore sessions, TimeProvider clock)
{
    public Task ExecuteAsync(Identity identity, CancellationToken cancellationToken)
    {
        if (identity.Path != AccessPath.BrowserSession)
        {
            throw Refusal.Forbidden();
        }

        return sessions.RevokeAsync(
            identity.AccessId,
            identity.OperatorId,
            clock.GetUtcNow(),
            cancellationToken);
    }
}
