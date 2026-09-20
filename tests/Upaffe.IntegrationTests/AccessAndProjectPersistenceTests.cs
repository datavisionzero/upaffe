using Microsoft.EntityFrameworkCore;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class AccessAndProjectPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostgreSQL_allows_exactly_one_operator()
    {
        await using var context = await MigratedContextAsync();
        context.Operators.Add(Operator.Establish("one@example.test", "hash-one", Noon));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Operators.Add(Operator.Establish("two@example.test", "hash-two", Noon));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Revoked_and_expired_access_state_round_trips()
    {
        await using var context = await MigratedContextAsync();
        var person = Operator.Establish("operator@example.test", "password-hash", Noon);
        var (session, _) = BrowserSession.Begin(person.Id, "browser", Noon);
        var credential = ManagementCredential.Create(person.Id, "automation", Noon);
        var (oldSecret, _) = ManagementCredentialSecret.Issue(credential.Id, Noon);
        session.Revoke(Noon.AddMinutes(1));
        credential.Revoke(Noon.AddMinutes(2));
        oldSecret.ExpireAt(Noon.AddMinutes(10));

        context.AddRange(person, session, credential, oldSecret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var storedSession = await context.BrowserSessions.SingleAsync(TestContext.Current.CancellationToken);
        var storedCredential = await context.ManagementCredentials.SingleAsync(TestContext.Current.CancellationToken);
        var storedSecret = await context.ManagementCredentialSecrets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(storedSession.IsValid(Noon.AddMinutes(3)));
        Assert.NotNull(storedCredential.RevokedAt);
        Assert.False(storedSecret.IsValid(Noon.AddMinutes(10)));
    }

    [Fact]
    public async Task Only_one_current_secret_exists_for_a_management_credential()
    {
        await using var context = await MigratedContextAsync();
        var person = Operator.Establish("operator@example.test", "password-hash", Noon);
        var credential = ManagementCredential.Create(person.Id, "automation", Noon);
        var (first, _) = ManagementCredentialSecret.Issue(credential.Id, Noon);
        context.AddRange(person, credential, first);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (second, _) = ManagementCredentialSecret.Issue(credential.Id, Noon.AddMinutes(1));
        context.Add(second);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_deleted_project_keeps_its_key_and_identity()
    {
        await using var context = await MigratedContextAsync();
        var project = Project.Create("backup-jobs", "Backup jobs", Noon);
        var id = project.Id;
        context.Projects.Add(project);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        project.Rename("Backups", Noon.AddMinutes(1));
        project.Delete(Noon.AddMinutes(2));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var stored = await context.Projects.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, stored.Id);
        Assert.Equal("backup-jobs", stored.Key);
        Assert.NotNull(stored.DeletedAt);

        context.Projects.Add(Project.Create("backup-jobs", "A replacement", Noon.AddMinutes(3)));
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private async Task<UpaffeDbContext> MigratedContextAsync()
    {
        var context = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        return context;
    }
}
