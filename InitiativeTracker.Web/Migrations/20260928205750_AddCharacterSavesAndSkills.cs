using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSavesAndSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CharismaSaveProficient",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ConstitutionSaveProficient",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DexteritySaveProficient",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IntelligenceSaveProficient",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StrengthSaveProficient",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WisdomSaveProficient",
                table: "PlayerCharacter",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PlayerCharacterSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Skill = table.Column<int>(type: "INTEGER", nullable: false),
                    ProficiencyLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    OverrideBonus = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterSkills_PlayerCharacter_PlayerCharacterId",
                        column: x => x.PlayerCharacterId,
                        principalTable: "PlayerCharacter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterSkills_PlayerCharacterId_Skill",
                table: "PlayerCharacterSkills",
                columns: new[] { "PlayerCharacterId", "Skill" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterSkills");

            migrationBuilder.DropColumn(
                name: "CharismaSaveProficient",
                table: "PlayerCharacter");

            migrationBuilder.DropColumn(
                name: "ConstitutionSaveProficient",
                table: "PlayerCharacter");

            migrationBuilder.DropColumn(
                name: "DexteritySaveProficient",
                table: "PlayerCharacter");

            migrationBuilder.DropColumn(
                name: "IntelligenceSaveProficient",
                table: "PlayerCharacter");

            migrationBuilder.DropColumn(
                name: "StrengthSaveProficient",
                table: "PlayerCharacter");

            migrationBuilder.DropColumn(
                name: "WisdomSaveProficient",
                table: "PlayerCharacter");
        }
    }
}
