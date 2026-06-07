using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildRaidLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "guild_id",
                table: "active_raids",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_active_raids_guild_id",
                table: "active_raids",
                column: "guild_id");

            migrationBuilder.AddForeignKey(
                name: "FK_active_raids_guilds_guild_id",
                table: "active_raids",
                column: "guild_id",
                principalTable: "guilds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_active_raids_guilds_guild_id",
                table: "active_raids");

            migrationBuilder.DropIndex(
                name: "ix_active_raids_guild_id",
                table: "active_raids");

            migrationBuilder.DropColumn(
                name: "guild_id",
                table: "active_raids");
        }
    }
}
