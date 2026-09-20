using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBootstrapGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bootstrap_grant");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bootstrap_grant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    armed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_singleton = table.Column<bool>(type: "boolean", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bootstrap_grant", x => x.id);
                    table.CheckConstraint("ck_bootstrap_consumed", "consumed_at is null or consumed_at >= armed_at");
                    table.CheckConstraint("ck_bootstrap_expiry", "expires_at > armed_at");
                    table.CheckConstraint("ck_bootstrap_hash", "octet_length(secret_hash) = 32");
                    table.CheckConstraint("ck_bootstrap_singleton", "is_singleton = true");
                });

            migrationBuilder.CreateIndex(
                name: "bootstrap_singleton",
                table: "bootstrap_grant",
                column: "is_singleton",
                unique: true);
        }
    }
}
