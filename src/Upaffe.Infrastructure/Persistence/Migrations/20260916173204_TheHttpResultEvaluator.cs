using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheHttpResultEvaluator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_http_monitor_failures",
                table: "http_monitor");

            migrationBuilder.AddColumn<Guid>(
                name: "failure_streak_start_id",
                table: "http_monitor",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                update http_monitor as m
                set failure_streak_start_id = coalesce(
                    (
                        select ranked.id
                        from (
                            select c.id,
                                   row_number() over (order by c.sequence desc) as position
                            from http_check as c
                            where c.monitor_id = m.id
                              and c.evaluation_generation = m.evaluation_generation
                              and c.outcome = 'Failure'
                              and c.sequence <= m.last_applied_sequence
                        ) as ranked
                        where ranked.position = m.consecutive_failures
                    ),
                    m.latest_result_id)
                where m.consecutive_failures > 0
                """);

            migrationBuilder.CreateIndex(
                name: "http_monitor_failure_streak_start",
                table: "http_monitor",
                column: "failure_streak_start_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_http_monitor_failures",
                table: "http_monitor",
                sql: "(consecutive_failures = 0 and failure_streak_start_id is null) or (consecutive_failures > 0 and failure_streak_start_id is not null)");

            migrationBuilder.AddForeignKey(
                name: "fk_http_monitor_failure_streak_start",
                table: "http_monitor",
                column: "failure_streak_start_id",
                principalTable: "http_check",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_http_monitor_failure_streak_start",
                table: "http_monitor");

            migrationBuilder.DropIndex(
                name: "http_monitor_failure_streak_start",
                table: "http_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_http_monitor_failures",
                table: "http_monitor");

            migrationBuilder.DropColumn(
                name: "failure_streak_start_id",
                table: "http_monitor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_http_monitor_failures",
                table: "http_monitor",
                sql: "consecutive_failures >= 0");
        }
    }
}
