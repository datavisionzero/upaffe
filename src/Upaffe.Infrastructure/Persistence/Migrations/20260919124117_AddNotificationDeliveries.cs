using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upaffe.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    recipient_key = table.Column<string>(type: "text", nullable: false),
                    project_key = table.Column<string>(type: "text", nullable: false),
                    project_name = table.Column<string>(type: "text", nullable: false),
                    monitor_key = table.Column<string>(type: "text", nullable: false),
                    monitor_name = table.Column<string>(type: "text", nullable: false),
                    monitor_type = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminal_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_delivery", x => x.id);
                    table.CheckConstraint("ck_notification_attempts", "attempt_count between 0 and 5");
                    table.CheckConstraint("ck_notification_kind", "kind in ('Alert', 'Recovery')");
                    table.CheckConstraint("ck_notification_monitor_type", "monitor_type in ('http', 'push')");
                    table.CheckConstraint("ck_notification_state", "state in ('Queued', 'Claimed', 'Retrying', 'Accepted', 'TerminalFailure', 'Obsolete')");
                });

            migrationBuilder.CreateIndex(
                name: "notification_delivery_due",
                table: "notification_delivery",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "notification_delivery_identity",
                table: "notification_delivery",
                columns: new[] { "incident_id", "kind", "recipient_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_delivery");
        }
    }
}
