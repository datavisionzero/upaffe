using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheHttpMonitoringModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "http_check",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_generation = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    response_time_milliseconds = table.Column<int>(type: "integer", nullable: true),
                    effective_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_http_check", x => x.id);
                    table.CheckConstraint("ck_http_check_effective_url", "effective_url is null or (char_length(effective_url) between 1 and 2048 and effective_url !~ '[?#]')");
                    table.CheckConstraint("ck_http_check_generation", "evaluation_generation > 0");
                    table.CheckConstraint("ck_http_check_outcome", "outcome is null or outcome in ('Success', 'Failure')");
                    table.CheckConstraint("ck_http_check_reason", "failure_reason is null or failure_reason ~ '^[a-z][a-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_http_check_response_time", "response_time_milliseconds is null or response_time_milliseconds >= 0");
                    table.CheckConstraint("ck_http_check_result", "(outcome is null and completed_at is null and failure_reason is null and status_code is null and response_time_milliseconds is null and effective_url is null) or (outcome = 'Success' and completed_at >= started_at and failure_reason is null and status_code is not null and response_time_milliseconds is not null and effective_url is not null) or (outcome = 'Failure' and completed_at >= started_at and failure_reason is not null)");
                    table.CheckConstraint("ck_http_check_sequence", "sequence > 0");
                    table.CheckConstraint("ck_http_check_started", "started_at >= scheduled_for");
                    table.CheckConstraint("ck_http_check_status", "status_code is null or status_code between 100 and 599");
                    table.CheckConstraint("ck_http_check_trigger", "trigger in ('Scheduled', 'Requested')");
                });

            migrationBuilder.CreateTable(
                name: "http_monitor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    has_target_query = table.Column<bool>(type: "boolean", nullable: false),
                    expected_status_code = table.Column<int>(type: "integer", nullable: false),
                    text_condition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text_fragment = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    failure_threshold = table.Column<int>(type: "integer", nullable: false),
                    instruction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    runbook_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    evaluation_generation = table.Column<long>(type: "bigint", nullable: false),
                    next_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_applied_sequence = table.Column<long>(type: "bigint", nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    latest_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latest_success_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paused_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_http_monitor", x => x.id);
                    table.CheckConstraint("ck_http_monitor_deleted", "deleted_at is null or (deleted_at >= created_at and next_check_at is null)");
                    table.CheckConstraint("ck_http_monitor_expected_status", "expected_status_code between 100 and 599");
                    table.CheckConstraint("ck_http_monitor_failures", "consecutive_failures >= 0");
                    table.CheckConstraint("ck_http_monitor_generation", "evaluation_generation > 0");
                    table.CheckConstraint("ck_http_monitor_interval", "interval_seconds between 30 and 2592000");
                    table.CheckConstraint("ck_http_monitor_key", "key ~ '^[a-z][a-z0-9-]{1,39}$'");
                    table.CheckConstraint("ck_http_monitor_latest", "latest_success_id is null or latest_result_id is not null");
                    table.CheckConstraint("ck_http_monitor_name", "char_length(btrim(name)) between 1 and 100");
                    table.CheckConstraint("ck_http_monitor_pause", "(state = 'Paused' and paused_at is not null and next_check_at is null) or (state <> 'Paused' and paused_at is null and (deleted_at is not null or next_check_at is not null))");
                    table.CheckConstraint("ck_http_monitor_sequence", "next_sequence > 0 and last_applied_sequence >= 0 and last_applied_sequence < next_sequence");
                    table.CheckConstraint("ck_http_monitor_state", "state in ('Untested', 'Healthy', 'Failing', 'Paused')");
                    table.CheckConstraint("ck_http_monitor_target", "char_length(target_url) between 1 and 2048 and target_url !~ '[?#]'");
                    table.CheckConstraint("ck_http_monitor_text", "(text_condition = 'None' and text_fragment is null) or (text_condition in ('Required', 'Forbidden') and char_length(text_fragment) between 1 and 4096)");
                    table.CheckConstraint("ck_http_monitor_threshold", "failure_threshold between 1 and 100");
                    table.CheckConstraint("ck_http_monitor_timeout", "timeout_seconds between 1 and 60 and timeout_seconds <= interval_seconds");
                    table.CheckConstraint("ck_http_monitor_updated", "updated_at >= created_at");
                    table.CheckConstraint("ck_http_monitor_version", "version > 0");
                    table.ForeignKey(
                        name: "fk_http_monitor_latest_result",
                        column: x => x.latest_result_id,
                        principalTable: "http_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_http_monitor_latest_success",
                        column: x => x.latest_success_id,
                        principalTable: "http_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_http_monitor_project",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "http_monitor_header",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_http_monitor_header", x => x.id);
                    table.CheckConstraint("ck_http_monitor_header_managed", "name not in ('connection', 'content-length', 'host', 'keep-alive', 'proxy-authenticate', 'proxy-authorization', 'te', 'trailer', 'transfer-encoding', 'upgrade')");
                    table.CheckConstraint("ck_http_monitor_header_name", "name ~ '^[!#$%&''*+.^_`|~0-9a-z-]{1,128}$'");
                    table.CheckConstraint("ck_http_monitor_header_updated", "updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_http_monitor_header_monitor",
                        column: x => x.monitor_id,
                        principalTable: "http_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "http_monitor_secret",
                columns: table => new
                {
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_query_utf8 = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_http_monitor_secret", x => x.monitor_id);
                    table.CheckConstraint("ck_http_monitor_secret_query", "target_query_utf8 is null or (octet_length(target_query_utf8) between 1 and 8192 and get_byte(target_query_utf8, 0) = 63)");
                    table.ForeignKey(
                        name: "fk_http_monitor_secret_monitor",
                        column: x => x.monitor_id,
                        principalTable: "http_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_failure_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opening_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latest_failure_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resolution_check_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_failure_sequence = table.Column<long>(type: "bigint", nullable: false),
                    opening_sequence = table.Column<long>(type: "bigint", nullable: false),
                    latest_failure_sequence = table.Column<long>(type: "bigint", nullable: false),
                    resolution_sequence = table.Column<long>(type: "bigint", nullable: true),
                    began_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    original_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    latest_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incident", x => x.id);
                    table.CheckConstraint("ck_incident_latest_reason", "latest_reason ~ '^[a-z][a-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_incident_original_reason", "original_reason ~ '^[a-z][a-z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_incident_resolution", "(resolved_at is null and resolution_check_id is null and resolution_sequence is null) or (resolved_at >= last_observed_at and resolution_check_id is not null and resolution_sequence > latest_failure_sequence)");
                    table.CheckConstraint("ck_incident_sequences", "first_failure_sequence > 0 and opening_sequence >= first_failure_sequence and latest_failure_sequence >= opening_sequence");
                    table.CheckConstraint("ck_incident_times", "began_at <= opened_at and began_at <= last_observed_at");
                    table.ForeignKey(
                        name: "fk_incident_first_failure",
                        column: x => x.first_failure_check_id,
                        principalTable: "http_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_latest_failure",
                        column: x => x.latest_failure_check_id,
                        principalTable: "http_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_monitor",
                        column: x => x.monitor_id,
                        principalTable: "http_monitor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_opening_check",
                        column: x => x.opening_check_id,
                        principalTable: "http_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incident_resolution_check",
                        column: x => x.resolution_check_id,
                        principalTable: "http_check",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "http_monitor_header_secret",
                columns: table => new
                {
                    header_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_utf8 = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_http_monitor_header_secret", x => x.header_id);
                    table.CheckConstraint("ck_http_monitor_header_secret_value", "octet_length(value_utf8) <= 4096");
                    table.ForeignKey(
                        name: "fk_http_monitor_header_secret_header",
                        column: x => x.header_id,
                        principalTable: "http_monitor_header",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "http_check_monitor_completed",
                table: "http_check",
                columns: new[] { "monitor_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "http_check_monitor_sequence",
                table: "http_check",
                columns: new[] { "monitor_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_http_monitor_latest_result_id",
                table: "http_monitor",
                column: "latest_result_id");

            migrationBuilder.CreateIndex(
                name: "IX_http_monitor_latest_success_id",
                table: "http_monitor",
                column: "latest_success_id");

            migrationBuilder.CreateIndex(
                name: "http_monitor_due",
                table: "http_monitor",
                column: "next_check_at",
                filter: "deleted_at is null and state <> 'Paused'");

            migrationBuilder.CreateIndex(
                name: "http_monitor_project_key",
                table: "http_monitor",
                columns: new[] { "project_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "http_monitor_header_monitor_name",
                table: "http_monitor_header",
                columns: new[] { "monitor_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incident_first_failure_check_id",
                table: "incident",
                column: "first_failure_check_id");

            migrationBuilder.CreateIndex(
                name: "IX_incident_latest_failure_check_id",
                table: "incident",
                column: "latest_failure_check_id");

            migrationBuilder.CreateIndex(
                name: "IX_incident_opening_check_id",
                table: "incident",
                column: "opening_check_id");

            migrationBuilder.CreateIndex(
                name: "IX_incident_resolution_check_id",
                table: "incident",
                column: "resolution_check_id");

            migrationBuilder.CreateIndex(
                name: "incident_monitor_opened",
                table: "incident",
                columns: new[] { "monitor_id", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "incident_one_open_per_monitor",
                table: "incident",
                column: "monitor_id",
                unique: true,
                filter: "resolved_at is null");

            migrationBuilder.AddForeignKey(
                name: "fk_http_check_monitor",
                table: "http_check",
                column: "monitor_id",
                principalTable: "http_monitor",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_http_check_monitor",
                table: "http_check");

            migrationBuilder.DropTable(
                name: "http_monitor_header_secret");

            migrationBuilder.DropTable(
                name: "http_monitor_secret");

            migrationBuilder.DropTable(
                name: "incident");

            migrationBuilder.DropTable(
                name: "http_monitor_header");

            migrationBuilder.DropTable(
                name: "http_monitor");

            migrationBuilder.DropTable(
                name: "http_check");
        }
    }
}
