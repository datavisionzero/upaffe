using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThePersistentHttpScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "execution_attempts",
                table: "http_check",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "execution_lease_token",
                table: "http_check",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "execution_lease_until",
                table: "http_check",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_execution_attempt_at",
                table: "http_check",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                update http_check
                set execution_lease_token = gen_random_uuid(),
                    execution_lease_until = started_at + interval '2 minutes',
                    execution_attempts = 1,
                    last_execution_attempt_at = started_at
                where trigger = 'Scheduled'
                """);

            migrationBuilder.CreateIndex(
                name: "http_check_expired_lease",
                table: "http_check",
                column: "execution_lease_until",
                filter: "trigger = 'Scheduled' and completed_at is null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_http_check_execution_lease",
                table: "http_check",
                sql: "(trigger = 'Requested' and execution_lease_token is null and execution_lease_until is null and execution_attempts = 0 and last_execution_attempt_at is null) or (trigger = 'Scheduled' and execution_lease_token is not null and execution_lease_until > last_execution_attempt_at and execution_attempts > 0 and last_execution_attempt_at >= started_at)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "http_check_expired_lease",
                table: "http_check");

            migrationBuilder.DropCheckConstraint(
                name: "ck_http_check_execution_lease",
                table: "http_check");

            migrationBuilder.DropColumn(
                name: "execution_attempts",
                table: "http_check");

            migrationBuilder.DropColumn(
                name: "execution_lease_token",
                table: "http_check");

            migrationBuilder.DropColumn(
                name: "execution_lease_until",
                table: "http_check");

            migrationBuilder.DropColumn(
                name: "last_execution_attempt_at",
                table: "http_check");
        }
    }
}
