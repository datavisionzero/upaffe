using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class NotificationDeliveryMapping : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_delivery", table =>
        {
            table.HasCheckConstraint("ck_notification_attempts", "attempt_count between 0 and 5");
            table.HasCheckConstraint("ck_notification_state", "state in ('Queued', 'Claimed', 'Retrying', 'Accepted', 'TerminalFailure', 'Obsolete')");
            table.HasCheckConstraint("ck_notification_kind", "kind in ('Alert', 'Recovery')");
            table.HasCheckConstraint("ck_notification_monitor_type", "monitor_type in ('http', 'push')");
        });
        builder.HasKey(value => value.Id).HasName("pk_notification_delivery");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.IncidentId).HasColumnName("incident_id");
        builder.Property(value => value.Kind).HasColumnName("kind").HasConversion<string>();
        builder.Property(value => value.Recipient).HasColumnName("recipient");
        builder.Property(value => value.RecipientKey).HasColumnName("recipient_key");
        builder.Property(value => value.ProjectKey).HasColumnName("project_key");
        builder.Property(value => value.ProjectName).HasColumnName("project_name");
        builder.Property(value => value.MonitorKey).HasColumnName("monitor_key");
        builder.Property(value => value.MonitorName).HasColumnName("monitor_name");
        builder.Property(value => value.MonitorType).HasColumnName("monitor_type");
        builder.Property(value => value.Reason).HasColumnName("reason");
        builder.Property(value => value.OccurredAt).HasColumnName("occurred_at");
        builder.Property(value => value.State).HasColumnName("state").HasConversion<string>();
        builder.Property(value => value.AttemptCount).HasColumnName("attempt_count");
        builder.Property(value => value.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(value => value.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(value => value.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(value => value.RecoveryDecisionAt).HasColumnName("recovery_decision_at");
        builder.Property(value => value.TerminalAt).HasColumnName("terminal_at");
        builder.Property(value => value.LeaseUntil).HasColumnName("lease_until");
        builder.Property(value => value.LeaseToken).HasColumnName("lease_token");
        builder.Property(value => value.ActiveAttemptToken).HasColumnName("active_attempt_token");
        builder.Property(value => value.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(64);
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(value => new { value.IncidentId, value.Kind, value.RecipientKey })
            .IsUnique().HasDatabaseName("notification_delivery_identity");
        builder.HasIndex(value => new { value.State, value.NextAttemptAt })
            .HasDatabaseName("notification_delivery_due");
    }
}
