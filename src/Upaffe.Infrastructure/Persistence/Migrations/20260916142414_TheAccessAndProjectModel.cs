using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TheAccessAndProjectModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bootstrap_grant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_singleton = table.Column<bool>(type: "boolean", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    armed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bootstrap_grant", x => x.id);
                    table.CheckConstraint("ck_bootstrap_consumed", "consumed_at is null or consumed_at >= armed_at");
                    table.CheckConstraint("ck_bootstrap_expiry", "expires_at > armed_at");
                    table.CheckConstraint("ck_bootstrap_hash", "octet_length(secret_hash) = 32");
                    table.CheckConstraint("ck_bootstrap_singleton", "is_singleton = true");
                });

            migrationBuilder.CreateTable(
                name: "operator_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_singleton = table.Column<bool>(type: "boolean", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_identity", x => x.id);
                    table.CheckConstraint("ck_operator_singleton", "is_singleton = true");
                });

            migrationBuilder.CreateTable(
                name: "project",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project", x => x.id);
                    table.CheckConstraint("ck_project_deleted", "deleted_at is null or deleted_at >= created_at");
                    table.CheckConstraint("ck_project_key", "key ~ '^[a-z][a-z0-9-]{1,39}$'");
                    table.CheckConstraint("ck_project_name", "char_length(btrim(name)) between 1 and 100");
                    table.CheckConstraint("ck_project_version", "version > 0");
                });

            migrationBuilder.CreateTable(
                name: "browser_session",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_browser_session", x => x.id);
                    table.CheckConstraint("ck_browser_session_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_browser_session_hash", "octet_length(secret_hash) = 32");
                    table.CheckConstraint("ck_browser_session_last_used", "last_used_at >= created_at and last_used_at <= expires_at");
                    table.CheckConstraint("ck_browser_session_revoked", "revoked_at is null or revoked_at >= created_at");
                    table.ForeignKey(
                        name: "fk_browser_session_operator",
                        column: x => x.operator_id,
                        principalTable: "operator_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "management_credential",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_management_credential", x => x.id);
                    table.CheckConstraint("ck_management_credential_revoked", "revoked_at is null or revoked_at >= created_at");
                    table.ForeignKey(
                        name: "fk_management_credential_operator",
                        column: x => x.operator_id,
                        principalTable: "operator_identity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "management_credential_secret",
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
                    table.PrimaryKey("pk_management_credential_secret", x => x.id);
                    table.CheckConstraint("ck_management_credential_secret_expiry", "expires_at is null or expires_at > created_at");
                    table.CheckConstraint("ck_management_credential_secret_hash", "octet_length(secret_hash) = 32");
                    table.ForeignKey(
                        name: "fk_management_credential_secret",
                        column: x => x.credential_id,
                        principalTable: "management_credential",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "bootstrap_singleton",
                table: "bootstrap_grant",
                column: "is_singleton",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "browser_session_operator",
                table: "browser_session",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "browser_session_secret_hash",
                table: "browser_session",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "management_credential_operator_name",
                table: "management_credential",
                columns: new[] { "operator_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "management_credential_current_secret",
                table: "management_credential_secret",
                column: "credential_id",
                unique: true,
                filter: "expires_at is null");

            migrationBuilder.CreateIndex(
                name: "management_credential_secret_hash",
                table: "management_credential_secret",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "operator_normalized_email",
                table: "operator_identity",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "operator_singleton",
                table: "operator_identity",
                column: "is_singleton",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "project_key",
                table: "project",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bootstrap_grant");

            migrationBuilder.DropTable(
                name: "browser_session");

            migrationBuilder.DropTable(
                name: "management_credential_secret");

            migrationBuilder.DropTable(
                name: "project");

            migrationBuilder.DropTable(
                name: "management_credential");

            migrationBuilder.DropTable(
                name: "operator_identity");
        }
    }
}
