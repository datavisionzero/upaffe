using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitorPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "push_monitor",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "http_monitor",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_push_monitor_purpose",
                table: "push_monitor",
                sql: "purpose is null or char_length(purpose) <= 240");

            migrationBuilder.AddCheckConstraint(
                name: "ck_http_monitor_purpose",
                table: "http_monitor",
                sql: "purpose is null or char_length(purpose) <= 240");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_push_monitor_purpose",
                table: "push_monitor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_http_monitor_purpose",
                table: "http_monitor");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "push_monitor");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "http_monitor");
        }
    }
}
