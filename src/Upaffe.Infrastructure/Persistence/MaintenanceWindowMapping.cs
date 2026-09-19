using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class MaintenanceWindowMapping : IEntityTypeConfiguration<MaintenanceWindow>
{
    public void Configure(EntityTypeBuilder<MaintenanceWindow> builder)
    {
        builder.ToTable("maintenance_window", table =>
        {
            table.HasCheckConstraint("ck_maintenance_scope", "scope_type in ('project', 'http', 'push')");
            table.HasCheckConstraint("ck_maintenance_version", "version > 0");
            table.HasCheckConstraint("ck_maintenance_interval", "ends_at > started_at");
            table.HasCheckConstraint("ck_maintenance_ended", "ended_at is null or (ended_at >= started_at and ended_at <= ends_at)");
        });
        builder.HasKey(value => value.Id).HasName("pk_maintenance_window");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.ScopeType).HasColumnName("scope_type");
        builder.Property(value => value.ScopeId).HasColumnName("scope_id");
        builder.Property(value => value.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(value => value.StartedAt).HasColumnName("started_at");
        builder.Property(value => value.EndsAt).HasColumnName("ends_at");
        builder.Property(value => value.EndedAt).HasColumnName("ended_at");
        builder.HasIndex(value => new { value.ScopeType, value.ScopeId, value.Version })
            .IsUnique().HasDatabaseName("maintenance_scope_version");
    }
}
