using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimedMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_window",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "text", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_window", x => x.id);
                    table.CheckConstraint("ck_maintenance_ended", "ended_at is null or (ended_at >= started_at and ended_at <= ends_at)");
                    table.CheckConstraint("ck_maintenance_interval", "ends_at > started_at");
                    table.CheckConstraint("ck_maintenance_scope", "scope_type in ('project', 'http', 'push')");
                    table.CheckConstraint("ck_maintenance_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "maintenance_scope_version",
                table: "maintenance_window",
                columns: new[] { "scope_type", "scope_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_window");
        }
    }
}
