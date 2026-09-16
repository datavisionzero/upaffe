using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class HttpMonitorConfiguration : IEntityTypeConfiguration<HttpMonitor>
{
    public void Configure(EntityTypeBuilder<HttpMonitor> builder)
    {
        builder.ToTable("http_monitor", table =>
        {
            table.HasCheckConstraint("ck_http_monitor_key", "key ~ '^[a-z][a-z0-9-]{1,39}$'");
            table.HasCheckConstraint("ck_http_monitor_name", "char_length(btrim(name)) between 1 and 100");
            table.HasCheckConstraint("ck_http_monitor_target", "char_length(target_url) between 1 and 2048 and target_url !~ '[?#]'");
            table.HasCheckConstraint("ck_http_monitor_expected_status", "expected_status_code between 100 and 599");
            table.HasCheckConstraint("ck_http_monitor_interval", "interval_seconds between 30 and 2592000");
            table.HasCheckConstraint("ck_http_monitor_timeout", "timeout_seconds between 1 and 60 and timeout_seconds <= interval_seconds");
            table.HasCheckConstraint("ck_http_monitor_threshold", "failure_threshold between 1 and 100");
            table.HasCheckConstraint(
                "ck_http_monitor_text",
                "(text_condition = 'None' and text_fragment is null) or "
                + "(text_condition in ('Required', 'Forbidden') and char_length(text_fragment) between 1 and 4096)");
            table.HasCheckConstraint("ck_http_monitor_state", "state in ('Untested', 'Healthy', 'Failing', 'Paused')");
            table.HasCheckConstraint("ck_http_monitor_generation", "evaluation_generation > 0");
            table.HasCheckConstraint("ck_http_monitor_sequence", "next_sequence > 0 and last_applied_sequence >= 0 and last_applied_sequence < next_sequence");
            table.HasCheckConstraint(
                "ck_http_monitor_failures",
                "(consecutive_failures = 0 and failure_streak_start_id is null) or "
                + "(consecutive_failures > 0 and failure_streak_start_id is not null)");
            table.HasCheckConstraint("ck_http_monitor_version", "version > 0");
            table.HasCheckConstraint("ck_http_monitor_updated", "updated_at >= created_at");
            table.HasCheckConstraint(
                "ck_http_monitor_pause",
                "(state = 'Paused' and paused_at is not null and next_check_at is null) or "
                + "(state <> 'Paused' and paused_at is null and (deleted_at is not null or next_check_at is not null))");
            table.HasCheckConstraint("ck_http_monitor_deleted", "deleted_at is null or (deleted_at >= created_at and next_check_at is null)");
            table.HasCheckConstraint("ck_http_monitor_latest", "latest_success_id is null or latest_result_id is not null");
        });
        builder.HasKey(value => value.Id).HasName("pk_http_monitor");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.ProjectId).HasColumnName("project_id");
        builder.Property(value => value.Key).HasColumnName("key").HasMaxLength(HttpMonitor.MaximumKeyLength);
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(HttpMonitor.MaximumNameLength);
        builder.Property(value => value.TargetUrl).HasColumnName("target_url").HasMaxLength(HttpMonitor.MaximumTargetLength);
        builder.Property(value => value.HasTargetQuery).HasColumnName("has_target_query");
        builder.Property(value => value.ExpectedStatusCode).HasColumnName("expected_status_code");
        builder.Property(value => value.TextCondition).HasColumnName("text_condition").HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.TextFragment).HasColumnName("text_fragment").HasMaxLength(HttpMonitor.MaximumTextFragmentLength);
        builder.Property(value => value.IntervalSeconds).HasColumnName("interval_seconds");
        builder.Property(value => value.TimeoutSeconds).HasColumnName("timeout_seconds");
        builder.Property(value => value.FailureThreshold).HasColumnName("failure_threshold");
        builder.Property(value => value.Instruction).HasColumnName("instruction").HasMaxLength(HttpMonitor.MaximumInstructionLength);
        builder.Property(value => value.RunbookUrl).HasColumnName("runbook_url").HasMaxLength(HttpMonitor.MaximumRunbookUrlLength);
        builder.Property(value => value.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.EvaluationGeneration).HasColumnName("evaluation_generation");
        builder.Property(value => value.NextSequence).HasColumnName("next_sequence");
        builder.Property(value => value.LastAppliedSequence).HasColumnName("last_applied_sequence");
        builder.Property(value => value.ConsecutiveFailures).HasColumnName("consecutive_failures");
        builder.Property(value => value.FailureStreakStartId).HasColumnName("failure_streak_start_id");
        builder.Property(value => value.NextCheckAt).HasColumnName("next_check_at");
        builder.Property(value => value.LatestResultId).HasColumnName("latest_result_id");
        builder.Property(value => value.LatestSuccessId).HasColumnName("latest_success_id");
        builder.Property(value => value.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        builder.Property(value => value.PausedAt).HasColumnName("paused_at");
        builder.Property(value => value.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(value => new { value.ProjectId, value.Key }).IsUnique().HasDatabaseName("http_monitor_project_key");
        builder.HasIndex(value => value.NextCheckAt).HasFilter("deleted_at is null and state <> 'Paused'")
            .HasDatabaseName("http_monitor_due");
        builder.HasIndex(value => value.FailureStreakStartId).HasDatabaseName("http_monitor_failure_streak_start");
        builder.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_http_monitor_project");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.LatestResultId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_http_monitor_latest_result");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.LatestSuccessId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_http_monitor_latest_success");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.FailureStreakStartId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_http_monitor_failure_streak_start");
    }
}

internal sealed class HttpMonitorSecretConfiguration : IEntityTypeConfiguration<HttpMonitorSecret>
{
    public void Configure(EntityTypeBuilder<HttpMonitorSecret> builder)
    {
        builder.ToTable("http_monitor_secret", table =>
            table.HasCheckConstraint(
                "ck_http_monitor_secret_query",
                "target_query_utf8 is null or (octet_length(target_query_utf8) between 1 and 8192 and get_byte(target_query_utf8, 0) = 63)"));
        builder.HasKey(value => value.MonitorId).HasName("pk_http_monitor_secret");
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id").ValueGeneratedNever();
        builder.Property(value => value.TargetQueryUtf8).HasColumnName("target_query_utf8");
        builder.HasOne<HttpMonitor>().WithOne().HasForeignKey<HttpMonitorSecret>(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_http_monitor_secret_monitor");
    }
}

internal sealed class HttpMonitorHeaderConfiguration : IEntityTypeConfiguration<HttpMonitorHeader>
{
    public void Configure(EntityTypeBuilder<HttpMonitorHeader> builder)
    {
        builder.ToTable("http_monitor_header", table =>
        {
            table.HasCheckConstraint("ck_http_monitor_header_name", "name ~ '^[!#$%&''*+.^_`|~0-9a-z-]{1,128}$'");
            table.HasCheckConstraint(
                "ck_http_monitor_header_managed",
                "name not in ('connection', 'content-length', 'host', 'keep-alive', 'proxy-authenticate', "
                + "'proxy-authorization', 'te', 'trailer', 'transfer-encoding', 'upgrade')");
            table.HasCheckConstraint("ck_http_monitor_header_updated", "updated_at >= created_at");
        });
        builder.HasKey(value => value.Id).HasName("pk_http_monitor_header");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id");
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(HttpMonitorHeader.MaximumNameLength);
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(value => new { value.MonitorId, value.Name }).IsUnique().HasDatabaseName("http_monitor_header_monitor_name");
        builder.HasOne<HttpMonitor>().WithMany().HasForeignKey(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_http_monitor_header_monitor");
    }
}

internal sealed class HttpMonitorHeaderSecretConfiguration : IEntityTypeConfiguration<HttpMonitorHeaderSecret>
{
    public void Configure(EntityTypeBuilder<HttpMonitorHeaderSecret> builder)
    {
        builder.ToTable("http_monitor_header_secret", table =>
            table.HasCheckConstraint("ck_http_monitor_header_secret_value", "octet_length(value_utf8) <= 4096"));
        builder.HasKey(value => value.HeaderId).HasName("pk_http_monitor_header_secret");
        builder.Property(value => value.HeaderId).HasColumnName("header_id").ValueGeneratedNever();
        builder.Property(value => value.ValueUtf8).HasColumnName("value_utf8");
        builder.HasOne<HttpMonitorHeader>().WithOne().HasForeignKey<HttpMonitorHeaderSecret>(value => value.HeaderId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_http_monitor_header_secret_header");
    }
}

internal sealed class HttpCheckConfiguration : IEntityTypeConfiguration<HttpCheck>
{
    public void Configure(EntityTypeBuilder<HttpCheck> builder)
    {
        builder.ToTable("http_check", table =>
        {
            table.HasCheckConstraint("ck_http_check_generation", "evaluation_generation > 0");
            table.HasCheckConstraint("ck_http_check_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_http_check_trigger", "trigger in ('Scheduled', 'Requested')");
            table.HasCheckConstraint("ck_http_check_started", "started_at >= scheduled_for");
            table.HasCheckConstraint(
                "ck_http_check_result",
                "(outcome is null and completed_at is null and failure_reason is null and status_code is null and response_time_milliseconds is null and effective_url is null) or "
                + "(outcome = 'Success' and completed_at >= started_at and failure_reason is null and status_code is not null and response_time_milliseconds is not null and effective_url is not null) or "
                + "(outcome = 'Failure' and completed_at >= started_at and failure_reason is not null)");
            table.HasCheckConstraint("ck_http_check_outcome", "outcome is null or outcome in ('Success', 'Failure')");
            table.HasCheckConstraint("ck_http_check_reason", "failure_reason is null or failure_reason ~ '^[a-z][a-z0-9_]{0,63}$'");
            table.HasCheckConstraint("ck_http_check_status", "status_code is null or status_code between 100 and 599");
            table.HasCheckConstraint("ck_http_check_response_time", "response_time_milliseconds is null or response_time_milliseconds >= 0");
            table.HasCheckConstraint("ck_http_check_effective_url", "effective_url is null or (char_length(effective_url) between 1 and 2048 and effective_url !~ '[?#]')");
            table.HasCheckConstraint(
                "ck_http_check_execution_lease",
                "(trigger = 'Requested' and execution_lease_token is null and execution_lease_until is null and execution_attempts = 0 and last_execution_attempt_at is null) or "
                + "(trigger = 'Scheduled' and execution_lease_token is not null and execution_lease_until > last_execution_attempt_at and execution_attempts > 0 and last_execution_attempt_at >= started_at)");
        });
        builder.HasKey(value => value.Id).HasName("pk_http_check");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id");
        builder.Property(value => value.EvaluationGeneration).HasColumnName("evaluation_generation");
        builder.Property(value => value.Sequence).HasColumnName("sequence");
        builder.Property(value => value.Trigger).HasColumnName("trigger").HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.ScheduledFor).HasColumnName("scheduled_for");
        builder.Property(value => value.StartedAt).HasColumnName("started_at");
        builder.Property(value => value.CompletedAt).HasColumnName("completed_at");
        builder.Property(value => value.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.FailureReason).HasColumnName("failure_reason").HasMaxLength(HttpCheck.MaximumReasonLength);
        builder.Property(value => value.StatusCode).HasColumnName("status_code");
        builder.Property(value => value.ResponseTimeMilliseconds).HasColumnName("response_time_milliseconds");
        builder.Property(value => value.EffectiveUrl).HasColumnName("effective_url").HasMaxLength(HttpCheck.MaximumEffectiveUrlLength);
        builder.Property(value => value.ExecutionLeaseToken).HasColumnName("execution_lease_token");
        builder.Property(value => value.ExecutionLeaseUntil).HasColumnName("execution_lease_until");
        builder.Property(value => value.ExecutionAttempts).HasColumnName("execution_attempts");
        builder.Property(value => value.LastExecutionAttemptAt).HasColumnName("last_execution_attempt_at");
        builder.Ignore(value => value.IsCompleted);
        builder.HasIndex(value => new { value.MonitorId, value.Sequence }).IsUnique().HasDatabaseName("http_check_monitor_sequence");
        builder.HasIndex(value => new { value.MonitorId, value.CompletedAt }).HasDatabaseName("http_check_monitor_completed");
        builder.HasIndex(value => value.ExecutionLeaseUntil).HasFilter("trigger = 'Scheduled' and completed_at is null")
            .HasDatabaseName("http_check_expired_lease");
        builder.HasOne<HttpMonitor>().WithMany().HasForeignKey(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_http_check_monitor");
    }
}

internal sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incident", table =>
        {
            table.HasCheckConstraint("ck_incident_times", "began_at <= opened_at and began_at <= last_observed_at");
            table.HasCheckConstraint(
                "ck_incident_sequences",
                "first_failure_sequence > 0 and opening_sequence >= first_failure_sequence and latest_failure_sequence >= opening_sequence");
            table.HasCheckConstraint(
                "ck_incident_resolution",
                "(resolved_at is null and resolution_check_id is null and resolution_sequence is null) or "
                + "(resolved_at >= last_observed_at and resolution_check_id is not null and resolution_sequence > latest_failure_sequence)");
            table.HasCheckConstraint("ck_incident_original_reason", "original_reason ~ '^[a-z][a-z0-9_]{0,63}$'");
            table.HasCheckConstraint("ck_incident_latest_reason", "latest_reason ~ '^[a-z][a-z0-9_]{0,63}$'");
        });
        builder.HasKey(value => value.Id).HasName("pk_incident");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.MonitorId).HasColumnName("monitor_id");
        builder.Property(value => value.FirstFailureCheckId).HasColumnName("first_failure_check_id");
        builder.Property(value => value.OpeningCheckId).HasColumnName("opening_check_id");
        builder.Property(value => value.LatestFailureCheckId).HasColumnName("latest_failure_check_id");
        builder.Property(value => value.ResolutionCheckId).HasColumnName("resolution_check_id");
        builder.Property(value => value.FirstFailureSequence).HasColumnName("first_failure_sequence");
        builder.Property(value => value.OpeningSequence).HasColumnName("opening_sequence");
        builder.Property(value => value.LatestFailureSequence).HasColumnName("latest_failure_sequence");
        builder.Property(value => value.ResolutionSequence).HasColumnName("resolution_sequence");
        builder.Property(value => value.BeganAt).HasColumnName("began_at");
        builder.Property(value => value.OpenedAt).HasColumnName("opened_at");
        builder.Property(value => value.LastObservedAt).HasColumnName("last_observed_at");
        builder.Property(value => value.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(value => value.OriginalReason).HasColumnName("original_reason").HasMaxLength(HttpCheck.MaximumReasonLength);
        builder.Property(value => value.LatestReason).HasColumnName("latest_reason").HasMaxLength(HttpCheck.MaximumReasonLength);
        builder.Ignore(value => value.IsOpen);
        builder.HasIndex(value => value.MonitorId).IsUnique().HasFilter("resolved_at is null")
            .HasDatabaseName("incident_one_open_per_monitor");
        builder.HasIndex(value => new { value.MonitorId, value.OpenedAt }).HasDatabaseName("incident_monitor_opened");
        builder.HasOne<HttpMonitor>().WithMany().HasForeignKey(value => value.MonitorId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_incident_monitor");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.FirstFailureCheckId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_incident_first_failure");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.OpeningCheckId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_incident_opening_check");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.LatestFailureCheckId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_incident_latest_failure");
        builder.HasOne<HttpCheck>().WithMany().HasForeignKey(value => value.ResolutionCheckId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_incident_resolution_check");
    }
}
