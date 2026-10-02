using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSpells : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerCharacterSpell",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    School = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CastingTime = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Range = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Duration = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    HasVerbalComponent = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasSomaticComponent = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasMaterialComponent = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaterialDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RequiresConcentration = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRitual = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPrepared = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasSpellAttackRoll = table.Column<bool>(type: "INTEGER", nullable: false),
                    SavingThrowAbility = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterSpell", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterSpell_PlayerCharacter_PlayerCharacterId",
                        column: x => x.PlayerCharacterId,
                        principalTable: "PlayerCharacter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerCharacterSpellcasting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpellcastingAbility = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalSpellAttackBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalSpellSaveDcBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterSpellcasting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterSpellcasting_PlayerCharacter_PlayerCharacterId",
                        column: x => x.PlayerCharacterId,
                        principalTable: "PlayerCharacter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterSpell_PlayerCharacterId_Level_SortOrder",
                table: "PlayerCharacterSpell",
                columns: new[] { "PlayerCharacterId", "Level", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterSpellcasting_PlayerCharacterId",
                table: "PlayerCharacterSpellcasting",
                column: "PlayerCharacterId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterSpell");

            migrationBuilder.DropTable(
                name: "PlayerCharacterSpellcasting");
        }
    }
}
