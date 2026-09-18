using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordPushReportApplicability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "applicable",
                table: "push_report",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "applicable",
                table: "push_report");
        }
    }
}
