using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterTemporaryHp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TemporaryHp",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemporaryHp",
                table: "PlayerCharacter");
        }
    }
}
