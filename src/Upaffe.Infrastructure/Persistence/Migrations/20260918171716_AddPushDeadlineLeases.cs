using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPushDeadlineLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_push_report_time",
                table: "push_report");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_monitor_receipt",
                table: "push_monitor");

            migrationBuilder.AddColumn<Guid>(
                name: "deadline_lease_token",
                table: "push_monitor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deadline_lease_until",
                table: "push_monitor",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "push_report_one_deadline_observation",
                table: "push_report",
                columns: new[] { "monitor_id", "evaluation_generation", "observed_at" },
                unique: true,
                filter: "is_deadline_observation");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_report_time",
                table: "push_report",
                sql: "(is_deadline_observation and observed_at <= received_at) or (not is_deadline_observation and observed_at <= received_at + interval '5 minutes' and observed_at >= received_at - interval '90 days')");

            migrationBuilder.CreateIndex(
                name: "push_monitor_deadline_lease",
                table: "push_monitor",
                column: "deadline_lease_until",
                filter: "deadline_lease_until is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_monitor_deadline_lease",
                table: "push_monitor",
                sql: "(deadline_lease_token is null and deadline_lease_until is null) or (deadline_lease_token is not null and deadline_lease_until is not null)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_monitor_receipt",
                table: "push_monitor",
                sql: "last_received_at is null or latest_report_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "push_report_one_deadline_observation",
                table: "push_report");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_report_time",
                table: "push_report");

            migrationBuilder.DropIndex(
                name: "push_monitor_deadline_lease",
                table: "push_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_monitor_deadline_lease",
                table: "push_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_push_monitor_receipt",
                table: "push_monitor");

            migrationBuilder.DropColumn(
                name: "deadline_lease_token",
                table: "push_monitor");

            migrationBuilder.DropColumn(
                name: "deadline_lease_until",
                table: "push_monitor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_report_time",
                table: "push_report",
                sql: "observed_at <= received_at + interval '5 minutes' and observed_at >= received_at - interval '90 days'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_monitor_receipt",
                table: "push_monitor",
                sql: "(last_received_at is null and latest_report_id is null) or (last_received_at is not null and latest_report_id is not null)");
        }
    }
}
