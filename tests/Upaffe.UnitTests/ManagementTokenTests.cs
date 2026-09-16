using Upaffe.Domain.Access;

namespace Upaffe.UnitTests;

public sealed class ManagementTokenTests
{
    [Fact]
    public void A_token_round_trips_its_public_identifier_and_secret_digest()
    {
        var id = Guid.NewGuid();
        var generated = SecretValue.Create();
        var token = ManagementToken.Format(id, generated.Secret);

        Assert.True(ManagementToken.TryParse(token, out var parsed, out var hash));
        Assert.Equal(id, parsed);
        Assert.Equal(generated.Hash, hash);
        Assert.DoesNotContain(generated.Secret, Convert.ToHexString(hash), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer something")]
    [InlineData("upaffe_not-an-id_not-a-secret")]
    public void Malformed_tokens_admit_nobody(string token) =>
        Assert.False(ManagementToken.TryParse(token, out _, out _));

    [Fact]
    public void The_empty_public_identifier_is_never_a_credential()
    {
        var token = $"upaffe_{new string('0', 32)}_{new string('A', 43)}";
        Assert.False(ManagementToken.TryParse(token, out _, out _));
    }
}
