using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FriendshipPartialUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_friendships_requester_id_addressee_id",
                table: "friendships");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_requester_id_addressee_id",
                table: "friendships",
                columns: new[] { "requester_id", "addressee_id" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_friendships_requester_id_addressee_id",
                table: "friendships");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_requester_id_addressee_id",
                table: "friendships",
                columns: new[] { "requester_id", "addressee_id" },
                unique: true);
        }
    }
}
