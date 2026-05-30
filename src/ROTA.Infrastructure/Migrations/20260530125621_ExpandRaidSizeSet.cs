using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROTA.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandRaidSizeSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remap existing rows: old Large=2 → new Large=3.
            // Medium=2 is a new value — no existing rows should have it.
            // Personal=0 and Small=1 are unchanged.
            migrationBuilder.Sql("UPDATE active_raids SET size = 3 WHERE size = 2;");

            migrationBuilder.AlterColumn<int>(
                name: "size",
                table: "active_raids",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "size",
                table: "active_raids",
                type: "integer",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3);

            // Reverse remap: new Large=3 → old Large=2.
            migrationBuilder.Sql("UPDATE active_raids SET size = 2 WHERE size = 3;");
        }
    }
}
