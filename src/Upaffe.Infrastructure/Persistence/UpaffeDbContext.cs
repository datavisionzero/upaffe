using Microsoft.EntityFrameworkCore;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>The single EF Core schema for upaffe.</summary>
public sealed class UpaffeDbContext(DbContextOptions<UpaffeDbContext> options) : DbContext(options)
{
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<BootstrapGrant> BootstrapGrants => Set<BootstrapGrant>();
    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();
    public DbSet<ManagementCredential> ManagementCredentials => Set<ManagementCredential>();
    public DbSet<ManagementCredentialSecret> ManagementCredentialSecrets => Set<ManagementCredentialSecret>();
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UpaffeDbContext).Assembly);
}
