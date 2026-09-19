using Microsoft.EntityFrameworkCore;
using Upaffe.Domain.Access;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;
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
    public DbSet<EmailConfiguration> EmailConfigurations => Set<EmailConfiguration>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<HttpMonitor> HttpMonitors => Set<HttpMonitor>();
    public DbSet<HttpMonitorSecret> HttpMonitorSecrets => Set<HttpMonitorSecret>();
    public DbSet<HttpMonitorHeader> HttpMonitorHeaders => Set<HttpMonitorHeader>();
    public DbSet<HttpMonitorHeaderSecret> HttpMonitorHeaderSecrets => Set<HttpMonitorHeaderSecret>();
    public DbSet<HttpCheck> HttpChecks => Set<HttpCheck>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<PushMonitor> PushMonitors => Set<PushMonitor>();
    public DbSet<PushReport> PushReports => Set<PushReport>();
    public DbSet<ReportingCredential> ReportingCredentials => Set<ReportingCredential>();
    public DbSet<ReportingCredentialSecret> ReportingCredentialSecrets => Set<ReportingCredentialSecret>();
    public DbSet<PushIncident> PushIncidents => Set<PushIncident>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UpaffeDbContext).Assembly);
}
