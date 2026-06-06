using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbound_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email_type = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    triggering_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    triggering_system = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    detail = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    send_status = table.Column<int>(type: "integer", nullable: false),
                    send_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_send_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    review_status = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_emails", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbound_emails_players_triggering_player_id",
                        column: x => x.triggering_player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbound_emails_created_at",
                table: "outbound_emails",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_emails_email_type",
                table: "outbound_emails",
                column: "email_type");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_emails_review_status",
                table: "outbound_emails",
                column: "review_status");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_emails_reviewed_by",
                table: "outbound_emails",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_emails_triggering_player_id",
                table: "outbound_emails",
                column: "triggering_player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_emails");
        }
    }
}
