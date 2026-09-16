using Upaffe.Application.Access;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.UnitTests;

public sealed class BrowserSessionActTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unknown_email_and_absent_operator_still_do_one_password_verification()
    {
        var knownHasher = new RecordingHasher(false);
        var knownStore = new SessionStore(new(
            Guid.NewGuid(),
            "operator@example.test",
            "OPERATOR@EXAMPLE.TEST",
            "$argon2id$known"));
        var unknownEmail = new SignIn(knownStore, knownHasher, new FixedClock(Noon));

        var first = await Assert.ThrowsAsync<Refusal>(() => unknownEmail.ExecuteAsync(
            "unknown@example.test", "presented password", null, TestContext.Current.CancellationToken));

        var absentHasher = new RecordingHasher(false);
        var absentOperator = new SignIn(new SessionStore(null), absentHasher, new FixedClock(Noon));
        var second = await Assert.ThrowsAsync<Refusal>(() => absentOperator.ExecuteAsync(
            "unknown@example.test", "presented password", null, TestContext.Current.CancellationToken));

        Assert.Equal("sign_in_rejected", first.Code);
        Assert.Equal("sign_in_rejected", second.Code);
        Assert.Equal(["$argon2id$known"], knownHasher.Hashes);
        Assert.Single(absentHasher.Hashes);
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", absentHasher.Hashes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_success_creates_a_fresh_session()
    {
        var operatorId = Guid.NewGuid();
        var store = new SessionStore(new(
            operatorId,
            "operator@example.test",
            "OPERATOR@EXAMPLE.TEST",
            "$argon2id$known"));
        var signIn = new SignIn(store, new RecordingHasher(true), new FixedClock(Noon));

        var first = await signIn.ExecuteAsync(
            "operator@example.test", "password", "browser", TestContext.Current.CancellationToken);
        var second = await signIn.ExecuteAsync(
            "operator@example.test", "password", "browser", TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Secret, second.Secret);
        Assert.NotEqual(first.Session.Id, second.Session.Id);
        Assert.Equal(2, store.Added.Count);
    }

    private sealed class RecordingHasher(bool answer) : IPasswordHasher
    {
        public List<string> Hashes { get; } = [];

        public Task<string> HashAsync(string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> VerifyAsync(
            string encodedHash,
            string password,
            CancellationToken cancellationToken)
        {
            Hashes.Add(encodedHash);
            return Task.FromResult(answer);
        }
    }

    private sealed class SessionStore(OperatorLogin? login) : IBrowserSessionStore
    {
        public List<BrowserSession> Added { get; } = [];

        public Task<OperatorLogin?> ReadOperatorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(login);

        public Task AddAsync(BrowserSession session, CancellationToken cancellationToken)
        {
            Added.Add(session);
            return Task.CompletedTask;
        }

        public Task<AdmittedBrowser?> AdmitAsync(
            byte[] secretHash,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RevokeAsync(
            Guid sessionId,
            Guid operatorId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
