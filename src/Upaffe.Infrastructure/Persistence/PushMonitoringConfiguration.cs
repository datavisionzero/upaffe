using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class PushMonitorConfiguration : IEntityTypeConfiguration<PushMonitor>
{
    public void Configure(EntityTypeBuilder<PushMonitor> builder)
    {
        builder.ToTable("push_monitor", table =>
        {
            table.HasCheckConstraint("ck_push_monitor_key", "key ~ '^[a-z][a-z0-9-]{1,39}$'");
            table.HasCheckConstraint("ck_push_monitor_name", "char_length(btrim(name)) between 1 and 100");
            table.HasCheckConstraint("ck_push_monitor_mode", "mode in ('JobCompletion', 'StateReport')");
            table.HasCheckConstraint("ck_push_monitor_interval", "interval_seconds between 30 and 31536000");
            table.HasCheckConstraint(
                "ck_push_monitor_tolerance",
                "tolerance_seconds between 0 and 2592000 and tolerance_seconds <= interval_seconds");
            table.HasCheckConstraint("ck_push_monitor_state", "state in ('Untested', 'Healthy', 'Failing', 'Paused')");
            table.HasCheckConstraint("ck_push_monitor_generation", "evaluation_generation > 0");
            table.HasCheckConstraint(
                "ck_push_monitor_sequence",
                "next_sequence > 0 and last_applied_sequence >= 0 and last_applied_sequence < next_sequence");
            table.HasCheckConstraint(
                "ck_push_monitor_applied",
                "(last_applied_sequence = 0 and last_applied_observed_at is null) or "
                + "(last_applied_sequence > 0 and last_applied_observed_at is not null)");
            table.HasCheckConstraint(
                "ck_push_monitor_receipt",
                "last_received_at is null or latest_report_id is not null");
            table.HasCheckConstraint("ck_push_monitor_latest", "latest_success_id is null or latest_report_id is not null");
            table.HasCheckConstraint(
                "ck_push_monitor_deadline_lease",
                "(deadline_lease_token is null and deadline_lease_until is null) or "
                + "(deadline_lease_token is not null and deadline_lease_until is not null)");
            table.HasCheckConstraint("ck_push_monitor_version", "version > 0");
            table.HasCheckConstraint("ck_push_monitor_updated", "updated_at >= created_at");
            table.HasCheckConstraint(
                "ck_push_monitor_pause",
                "(state = 'Paused' and paused_at is not null and next_deadline_at is null) or "
                + "(state <> 'Paused' and paused_at is null and (deleted_at is not null or next_deadline_at is not null))");
            table.HasCheckConstraint(
                "ck_push_monitor_deleted",
                "deleted_at is null or (deleted_at >= created_at and next_deadline_at is null)");
        });
        builder.HasKey(value => value.Id).HasName("pk_push_monitor");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.ProjectId).HasColumnName("project_id");
        builder.Property(value => value.Key).HasColumnName("key").HasMaxLength(PushMonitor.MaximumKeyLength);
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(PushMonitor.MaximumNameLength);
        builder.Property(value => value.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(20);
        builder.Property(value => value.IntervalSeconds).HasColumnName("interval_seconds");
        builder.Property(value => value.ToleranceSeconds).HasColumnName("tolerance_seconds");
        builder.Property(value => value.Instruction).HasColumnName("instruction").HasMaxLength(PushMonitor.MaximumInstructionLength);
        builder.Property(value => value.RunbookUrl).HasColumnName("runbook_url").HasMaxLength(PushMonitor.MaximumRunbookUrlLength);
        builder.Property(value => value.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.EvaluationGeneration).HasColumnName("evaluation_generation");
        builder.Property(value => value.NextSequence).HasColumnName("next_sequence");
        builder.Property(value => value.LastAppliedSequence).HasColumnName("last_applied_sequence");
        builder.Property(value => value.LastAppliedObservedAt).HasColumnName("last_applied_observed_at");
        builder.Property(value => value.LastReceivedAt).HasColumnName("last_received_at");
        builder.Property(value => value.NextDeadlineAt).HasColumnName("next_deadline_at");
        builder.Property(value => value.LatestReportId).HasColumnName("latest_report_id");
        builder.Property(value => value.LatestSuccessId).HasColumnName("latest_success_id");
        builder.Property(value => value.DeadlineLeaseToken).HasColumnName("deadline_lease_token");
        builder.Property(value => value.DeadlineLeaseUntil).HasColumnName("deadline_lease_until");
        builder.Property(value => value.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        builder.Property(value => value.PausedAt).HasColumnName("paused_at");
        builder.Property(value => value.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(value => new { value.ProjectId, value.Key }).IsUnique().HasDatabaseName("push_monitor_project_key");
        builder.HasIndex(value => value.NextDeadlineAt).HasFilter("deleted_at is null and state <> 'Paused'")
            .HasDatabaseName("push_monitor_due");
        builder.HasIndex(value => value.DeadlineLeaseUntil).HasFilter("deadline_lease_until is not null")
            .HasDatabaseName("push_monitor_deadline_lease");
        builder.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_monitor_project");
        builder.HasOne<PushReport>().WithMany().HasForeignKey(value => value.LatestReportId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_monitor_latest_report");
        builder.HasOne<PushReport>().WithMany().HasForeignKey(value => value.LatestSuccessId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_monitor_latest_success");
    }
}

internal sealed class PushReportConfiguration : IEntityTypeConfiguration<PushReport>
{
    public void Configure(EntityTypeBuilder<PushReport> builder)
    {
        builder.ToTable("push_report", table =>
        {
            table.HasCheckConstraint("ck_push_report_generation", "evaluation_generation > 0");
            table.HasCheckConstraint("ck_push_report_sequence", "sequence > 0");
            table.HasCheckConstraint(
                "ck_push_report_time",
                "(is_deadline_observation and observed_at <= received_at) or "
                + "(not is_deadline_observation and observed_at <= received_at + interval '5 minutes' "
                + "and observed_at >= received_at - interval '90 days')");
            table.HasCheckConstraint("ck_push_report_outcome", "outcome in ('Success', 'Failure')");
            table.HasCheckConstraint(
                "ck_push_report_diagnostic",
                "(outcome = 'Success' and diagnostic_reason is null) or outcome = 'Failure'");
            table.HasCheckConstraint(
                "ck_push_report_deadline",
                "(not is_deadline_observation) or (outcome = 'Failure' and diagnostic_reason = 'report_missing')");
        });
        builder.HasKey(value => value.Id).HasName("pk_push_report");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id");
        builder.Property(value => value.ReportId).HasColumnName("report_id");
        builder.Property(value => value.EvaluationGeneration).HasColumnName("evaluation_generation");
        builder.Property(value => value.Sequence).HasColumnName("sequence");
        builder.Property(value => value.ObservedAt).HasColumnName("observed_at");
        builder.Property(value => value.ReceivedAt).HasColumnName("received_at");
        builder.Property(value => value.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.DiagnosticReason).HasColumnName("diagnostic_reason").HasMaxLength(PushReport.MaximumDiagnosticReasonLength);
        builder.Property(value => value.IsDeadlineObservation).HasColumnName("is_deadline_observation");
        builder.Property(value => value.Applicable).HasColumnName("applicable");
        builder.HasIndex(value => new { value.MonitorId, value.ReportId }).IsUnique()
            .HasDatabaseName("push_report_monitor_report_id");
        builder.HasIndex(value => new { value.MonitorId, value.Sequence }).IsUnique()
            .HasDatabaseName("push_report_monitor_sequence");
        builder.HasIndex(value => new { value.MonitorId, value.ReceivedAt })
            .HasDatabaseName("push_report_monitor_received");
        builder.HasIndex(value => new { value.MonitorId, value.EvaluationGeneration, value.ObservedAt }).IsUnique()
            .HasFilter("is_deadline_observation")
            .HasDatabaseName("push_report_one_deadline_observation");
        builder.HasOne<PushMonitor>().WithMany().HasForeignKey(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_report_monitor");
    }
}

internal sealed class ReportingCredentialConfiguration : IEntityTypeConfiguration<ReportingCredential>
{
    public void Configure(EntityTypeBuilder<ReportingCredential> builder)
    {
        builder.ToTable("reporting_credential", table =>
            table.HasCheckConstraint("ck_reporting_credential_revoked", "revoked_at is null or revoked_at >= created_at"));
        builder.HasKey(value => value.Id).HasName("pk_reporting_credential");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.RotatedAt).HasColumnName("rotated_at");
        builder.Property(value => value.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(value => value.MonitorId).IsUnique().HasDatabaseName("reporting_credential_monitor");
        builder.HasOne<PushMonitor>().WithOne().HasForeignKey<ReportingCredential>(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_reporting_credential_monitor");
    }
}

internal sealed class ReportingCredentialSecretConfiguration : IEntityTypeConfiguration<ReportingCredentialSecret>
{
    public void Configure(EntityTypeBuilder<ReportingCredentialSecret> builder)
    {
        builder.ToTable("reporting_credential_secret", table =>
        {
            table.HasCheckConstraint("ck_reporting_credential_secret_hash", "octet_length(secret_hash) = 32");
            table.HasCheckConstraint("ck_reporting_credential_secret_expiry", "expires_at is null or expires_at > created_at");
        });
        builder.HasKey(value => value.Id).HasName("pk_reporting_credential_secret");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.CredentialId).HasColumnName("credential_id");
        builder.Property(value => value.SecretHash).HasColumnName("secret_hash");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.ExpiresAt).HasColumnName("expires_at");
        builder.HasIndex(value => value.SecretHash).IsUnique().HasDatabaseName("reporting_credential_secret_hash");
        builder.HasIndex(value => value.CredentialId).IsUnique().HasFilter("expires_at is null")
            .HasDatabaseName("reporting_credential_current_secret");
        builder.HasOne<ReportingCredential>().WithMany().HasForeignKey(value => value.CredentialId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_reporting_credential_secret");
    }
}

internal sealed class PushIncidentConfiguration : IEntityTypeConfiguration<PushIncident>
{
    public void Configure(EntityTypeBuilder<PushIncident> builder)
    {
        builder.ToTable("push_incident", table =>
        {
            table.HasCheckConstraint(
                "ck_push_incident_times",
                "began_at <= opened_at + interval '5 minutes' and began_at <= last_observed_at");
            table.HasCheckConstraint(
                "ck_push_incident_sequences",
                "opening_sequence > 0 and latest_failure_sequence >= opening_sequence");
            table.HasCheckConstraint(
                "ck_push_incident_resolution",
                "(resolved_at is null and resolution_report_id is null and resolution_sequence is null) or "
                + "(resolved_at is not null and resolution_report_id is not null and resolution_sequence > latest_failure_sequence)");
            table.HasCheckConstraint("ck_push_incident_original_reason", "original_reason in ('reported_failure', 'report_missing')");
            table.HasCheckConstraint("ck_push_incident_latest_reason", "latest_reason in ('reported_failure', 'report_missing')");
        });
        builder.HasKey(value => value.Id).HasName("pk_push_incident");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id");
        builder.Property(value => value.OpeningReportId).HasColumnName("opening_report_id");
        builder.Property(value => value.LatestFailureReportId).HasColumnName("latest_failure_report_id");
        builder.Property(value => value.ResolutionReportId).HasColumnName("resolution_report_id");
        builder.Property(value => value.OpeningSequence).HasColumnName("opening_sequence");
        builder.Property(value => value.LatestFailureSequence).HasColumnName("latest_failure_sequence");
        builder.Property(value => value.ResolutionSequence).HasColumnName("resolution_sequence");
        builder.Property(value => value.BeganAt).HasColumnName("began_at");
        builder.Property(value => value.OpenedAt).HasColumnName("opened_at");
        builder.Property(value => value.LastObservedAt).HasColumnName("last_observed_at");
        builder.Property(value => value.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(value => value.OriginalReason).HasColumnName("original_reason").HasMaxLength(32);
        builder.Property(value => value.LatestReason).HasColumnName("latest_reason").HasMaxLength(32);
        builder.Property(value => value.NotificationDecisionAt).HasColumnName("notification_decision_at");
        builder.Ignore(value => value.IsOpen);
        builder.HasIndex(value => value.MonitorId).IsUnique().HasFilter("resolved_at is null")
            .HasDatabaseName("push_incident_one_open_per_monitor");
        builder.HasIndex(value => new { value.MonitorId, value.OpenedAt }).HasDatabaseName("push_incident_monitor_opened");
        builder.HasOne<PushMonitor>().WithMany().HasForeignKey(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_incident_monitor");
        builder.HasOne<PushReport>().WithMany().HasForeignKey(value => value.OpeningReportId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_incident_opening_report");
        builder.HasOne<PushReport>().WithMany().HasForeignKey(value => value.LatestFailureReportId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_incident_latest_failure_report");
        builder.HasOne<PushReport>().WithMany().HasForeignKey(value => value.ResolutionReportId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_push_incident_resolution_report");
    }
}
