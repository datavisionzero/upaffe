using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordHttpCheckApplicability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "applied_to_current_state",
                table: "http_check",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "applied_to_current_state",
                table: "http_check");
        }
    }
}
