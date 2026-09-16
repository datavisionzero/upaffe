using Upaffe.Infrastructure.Security;

namespace Upaffe.UnitTests;

public sealed class Argon2idPasswordHasherTests
{
    private readonly Argon2idPasswordHasher _hasher = new();

    [Fact]
    public async Task A_password_is_salted_and_verifiable()
    {
        var one = await _hasher.HashAsync("a sufficiently long password", TestContext.Current.CancellationToken);
        var two = await _hasher.HashAsync("a sufficiently long password", TestContext.Current.CancellationToken);

        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", one, StringComparison.Ordinal);
        Assert.NotEqual(one, two);
        Assert.True(await _hasher.VerifyAsync(
            one, "a sufficiently long password", TestContext.Current.CancellationToken));
        Assert.False(await _hasher.VerifyAsync(
            one, "a different long password", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("$argon2id$v=19$m=99999999,t=3,p=1$c2FsdA==$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=1$not-base64$aGFzaA==")]
    [InlineData("$argon2id$v=19$m=65536,m=65536,t=3,p=1$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    public async Task An_unreadable_or_unbounded_hash_admits_nobody(string encoded) =>
        Assert.False(await _hasher.VerifyAsync(
            encoded, "a sufficiently long password", TestContext.Current.CancellationToken));
}
