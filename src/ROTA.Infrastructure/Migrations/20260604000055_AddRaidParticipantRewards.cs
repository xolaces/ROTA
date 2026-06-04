using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRaidParticipantRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contribution_tier",
                table: "raid_participants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "gems_earned",
                table: "raid_participants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "gold_earned",
                table: "raid_participants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "items_earned_json",
                table: "raid_participants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rewarded_at",
                table: "raid_participants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stat_points_earned",
                table: "raid_participants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "xp_earned",
                table: "raid_participants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_raid_participants_player_rewarded_at",
                table: "raid_participants",
                columns: new[] { "player_id", "rewarded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_raid_participants_player_rewarded_at",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "contribution_tier",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "gems_earned",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "gold_earned",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "items_earned_json",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "rewarded_at",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "stat_points_earned",
                table: "raid_participants");

            migrationBuilder.DropColumn(
                name: "xp_earned",
                table: "raid_participants");
        }
    }
}
