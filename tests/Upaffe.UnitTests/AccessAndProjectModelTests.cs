using System.Text;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;

namespace Upaffe.UnitTests;

public sealed class AccessAndProjectModelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_operator_is_normalized_without_becoming_another_account()
    {
        var created = Operator.Establish(" Operator@Example.Test ", "$argon2id$not-a-plain-password", Noon);

        Assert.True(created.IsSingleton);
        Assert.Equal("Operator@Example.Test", created.Email);
        Assert.Equal("OPERATOR@EXAMPLE.TEST", created.NormalizedEmail);
        Assert.Throws<ArgumentException>(() => Operator.Establish("not-an-email", "hash", Noon));
    }

    [Fact]
    public void A_browser_session_keeps_only_a_digest_and_obeys_both_lifetimes()
    {
        var (session, secret) = BrowserSession.Begin(Guid.NewGuid(), " test browser ", Noon);

        Assert.Equal(SecretValue.Hash(secret), session.SecretHash);
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(session.SecretHash), StringComparison.Ordinal);
        Assert.True(session.IsValid(Noon.Add(BrowserSession.IdleLifetime).AddTicks(-1)));
        Assert.False(session.IsValid(Noon.Add(BrowserSession.IdleLifetime)));
        Assert.False(session.IsValid(Noon.Add(BrowserSession.AbsoluteLifetime)));
        Assert.Equal("test browser", session.Description);
    }

    [Fact]
    public void Revocation_is_immediate_and_idempotent()
    {
        var (session, _) = BrowserSession.Begin(Guid.NewGuid(), null, Noon);

        session.Revoke(Noon.AddMinutes(1));
        session.Revoke(Noon.AddMinutes(2));

        Assert.False(session.IsValid(Noon.AddMinutes(1)));
        Assert.Equal(Noon.AddMinutes(1), session.RevokedAt);
    }

    [Fact]
    public void A_management_secret_has_a_digest_and_an_explicit_overlap_end()
    {
        var credential = ManagementCredential.Create(Guid.NewGuid(), "deployment agent", Noon);
        var (issued, value) = ManagementCredentialSecret.Issue(credential.Id, Noon);

        Assert.Equal(SecretValue.Hash(value), issued.SecretHash);
        Assert.True(issued.IsValid(Noon.AddDays(1)));

        issued.ExpireAt(Noon.AddMinutes(10));
        Assert.True(issued.IsValid(Noon.AddMinutes(10).AddTicks(-1)));
        Assert.False(issued.IsValid(Noon.AddMinutes(10)));
    }

    [Fact]
    public void A_project_keeps_its_identity_across_rename_delete_and_restore()
    {
        var project = Project.Create("backup-jobs", "Backup jobs", Noon);
        var id = project.Id;

        project.Rename("Backups", Noon.AddMinutes(1));
        project.Delete(Noon.AddMinutes(2));
        project.Restore(Noon.AddMinutes(3));

        Assert.Equal(id, project.Id);
        Assert.Equal("backup-jobs", project.Key);
        Assert.Equal("Backups", project.Name);
        Assert.Null(project.DeletedAt);
        Assert.Equal(4, project.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Upper-case")]
    [InlineData("starts_with_underscore")]
    [InlineData("a-key-that-is-more-than-forty-characters-long")]
    public void Project_keys_have_one_canonical_shape(string key) =>
        Assert.Throws<ArgumentException>(() => Project.Create(key, "A project", Noon));
}
