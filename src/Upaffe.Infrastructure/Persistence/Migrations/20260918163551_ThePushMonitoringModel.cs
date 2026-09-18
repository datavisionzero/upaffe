using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThePushMonitoringModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_incident",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latest_failure_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolution_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opening_sequence = table.Column<long>(type: "bigint", nullable: false),
                    latest_failure_sequence = table.Column<long>(type: "bigint", nullable: false),
                    resolution_sequence = table.Column<long>(type: "bigint", nullable: true),
                    began_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    original_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    latest_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_incident", x => x.id);
                    table.CheckConstraint("ck_push_incident_latest_reason", "latest_reason in ('reported_failure', 'report_missing')");
                    table.CheckConstraint("ck_push_incident_original_reason", "original_reason in ('reported_failure', 'report_missing')");
                    table.CheckConstraint("ck_push_incident_resolution", "(resolved_at is null and resolution_report_id is null and resolution_sequence is null) or (resolved_at is not null and resolution_report_id is not null and resolution_sequence > latest_failure_sequence)");
                    table.CheckConstraint("ck_push_incident_sequences", "opening_sequence > 0 and latest_failure_sequence >= opening_sequence");
                    table.CheckConstraint("ck_push_incident_times", "began_at <= opened_at + interval '5 minutes' and began_at <= last_observed_at");
                });

            migrationBuilder.CreateTable(
                name: "push_monitor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    tolerance_seconds = table.Column<int>(type: "integer", nullable: false),
                    instruction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    runbook_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    evaluation_generation = table.Column<long>(type: "bigint", nullable: false),
                    next_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_applied_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_applied_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    latest_report_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latest_success_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_monitor", x => x.id);
                    table.CheckConstraint("ck_push_monitor_applied", "(last_applied_sequence = 0 and last_applied_observed_at is null) or (last_applied_sequence > 0 and last_applied_observed_at is not null)");
                    table.CheckConstraint("ck_push_monitor_deleted", "deleted_at is null or (deleted_at >= created_at and next_deadline_at is null)");
                    table.CheckConstraint("ck_push_monitor_generation", "evaluation_generation > 0");
                    table.CheckConstraint("ck_push_monitor_interval", "interval_seconds between 30 and 31536000");
                    table.CheckConstraint("ck_push_monitor_key", "key ~ '^[a-z][a-z0-9-]{1,39}$'");
                    table.CheckConstraint("ck_push_monitor_latest", "latest_success_id is null or latest_report_id is not null");
                    table.CheckConstraint("ck_push_monitor_mode", "mode in ('JobCompletion', 'StateReport')");
                    table.CheckConstraint("ck_push_monitor_name", "char_length(btrim(name)) between 1 and 100");
                    table.CheckConstraint("ck_push_monitor_pause", "(state = 'Paused' and paused_at is not null and next_deadline_at is null) or (state <> 'Paused' and paused_at is null and (deleted_at is not null or next_deadline_at is not null))");
                    table.CheckConstraint("ck_push_monitor_receipt", "(last_received_at is null and latest_report_id is null) or (last_received_at is not null and latest_report_id is not null)");
                    table.CheckConstraint("ck_push_monitor_sequence", "next_sequence > 0 and last_applied_sequence >= 0 and last_applied_sequence < next_sequence");
                    table.CheckConstraint("ck_push_monitor_state", "state in ('Untested', 'Healthy', 'Failing', 'Paused')");
                    table.CheckConstraint("ck_push_monitor_tolerance", "tolerance_seconds between 0 and 2592000 and tolerance_seconds <= interval_seconds");
                    table.CheckConstraint("ck_push_monitor_updated", "updated_at >= created_at");
                    table.CheckConstraint("ck_push_monitor_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_push_monitor_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "push_report",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_generation = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    diagnostic_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_deadline_observation = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_report", x => x.id);
                    table.CheckConstraint("ck_push_report_deadline", "(not is_deadline_observation) or (outcome = 'Failure' and diagnostic_reason = 'report_missing')");
                    table.CheckConstraint("ck_push_report_diagnostic", "(outcome = 'Success' and diagnostic_reason is null) or outcome = 'Failure'");
                    table.CheckConstraint("ck_push_report_generation", "evaluation_generation > 0");
                    table.CheckConstraint("ck_push_report_outcome", "outcome in ('Success', 'Failure')");
                    table.CheckConstraint("ck_push_report_sequence", "sequence > 0");
                    table.CheckConstraint("ck_push_report_time", "observed_at <= received_at + interval '5 minutes' and observed_at >= received_at - interval '90 days'");
                    table.ForeignKey(
                        name: "fk_push_report_monitor",
                        column: x => x.monitor_id,
                        principalTable: "push_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reporting_credential",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_credential", x => x.id);
                    table.CheckConstraint("ck_reporting_credential_revoked", "revoked_at is null or revoked_at >= created_at");
                    table.ForeignKey(
                        name: "fk_reporting_credential_monitor",
                        column: x => x.monitor_id,
                        principalTable: "push_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reporting_credential_secret",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_credential_secret", x => x.id);
                    table.CheckConstraint("ck_reporting_credential_secret_expiry", "expires_at is null or expires_at > created_at");
                    table.CheckConstraint("ck_reporting_credential_secret_hash", "octet_length(secret_hash) = 32");
                    table.ForeignKey(
                        name: "fk_reporting_credential_secret",
                        column: x => x.credential_id,
                        principalTable: "reporting_credential",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_incident_latest_failure_report_id",
                table: "push_incident",
                column: "latest_failure_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_incident_opening_report_id",
                table: "push_incident",
                column: "opening_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_incident_resolution_report_id",
                table: "push_incident",
                column: "resolution_report_id");

            migrationBuilder.CreateIndex(
                name: "push_incident_monitor_opened",
                table: "push_incident",
                columns: new[] { "monitor_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "push_incident_one_open_per_monitor",
                table: "push_incident",
                column: "monitor_id",
                unique: true,
                filter: "resolved_at is null");

            migrationBuilder.CreateIndex(
                name: "IX_push_monitor_latest_report_id",
                table: "push_monitor",
                column: "latest_report_id");

            migrationBuilder.CreateIndex(
                name: "IX_push_monitor_latest_success_id",
                table: "push_monitor",
                column: "latest_success_id");

            migrationBuilder.CreateIndex(
                name: "push_monitor_due",
                table: "push_monitor",
                column: "next_deadline_at",
                filter: "deleted_at is null and state <> 'Paused'");

            migrationBuilder.CreateIndex(
                name: "push_monitor_project_key",
                table: "push_monitor",
                columns: new[] { "project_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "push_report_monitor_received",
                table: "push_report",
                columns: new[] { "monitor_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "push_report_monitor_report_id",
                table: "push_report",
                columns: new[] { "monitor_id", "report_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "push_report_monitor_sequence",
                table: "push_report",
                columns: new[] { "monitor_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "reporting_credential_monitor",
                table: "reporting_credential",
                column: "monitor_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "reporting_credential_current_secret",
                table: "reporting_credential_secret",
                column: "credential_id",
                unique: true,
                filter: "expires_at is null");

            migrationBuilder.CreateIndex(
                name: "reporting_credential_secret_hash",
                table: "reporting_credential_secret",
                column: "secret_hash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_push_incident_latest_failure_report",
                table: "push_incident",
                column: "latest_failure_report_id",
                principalTable: "push_report",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_push_incident_opening_report",
                table: "push_incident",
                column: "opening_report_id",
                principalTable: "push_report",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_push_incident_resolution_report",
                table: "push_incident",
                column: "resolution_report_id",
                principalTable: "push_report",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_push_incident_monitor",
                table: "push_incident",
                column: "monitor_id",
                principalTable: "push_monitor",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_push_monitor_latest_report",
                table: "push_monitor",
                column: "latest_report_id",
                principalTable: "push_report",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_push_monitor_latest_success",
                table: "push_monitor",
                column: "latest_success_id",
                principalTable: "push_report",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_push_monitor_latest_report",
                table: "push_monitor");

            migrationBuilder.DropForeignKey(
                name: "fk_push_monitor_latest_success",
                table: "push_monitor");

            migrationBuilder.DropTable(
                name: "push_incident");

            migrationBuilder.DropTable(
                name: "reporting_credential_secret");

            migrationBuilder.DropTable(
                name: "reporting_credential");

            migrationBuilder.DropTable(
                name: "push_report");

            migrationBuilder.DropTable(
                name: "push_monitor");
        }
    }
}
