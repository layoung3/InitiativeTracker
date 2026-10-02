using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSpellEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Area",
                table: "PlayerCharacterSpell",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerCharacterSpellEffectComponent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterSpellId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectType = table.Column<int>(type: "INTEGER", nullable: false),
                    Expression = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DamageType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AddsSpellcastingAbilityModifier = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdditionalBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    UpcastExpressionPerLevel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterSpellEffectComponent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterSpellEffectComponent_PlayerCharacterSpell_PlayerCharacterSpellId",
                        column: x => x.PlayerCharacterSpellId,
                        principalTable: "PlayerCharacterSpell",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterSpellEffectComponent_PlayerCharacterSpellId_SortOrder",
                table: "PlayerCharacterSpellEffectComponent",
                columns: new[] { "PlayerCharacterSpellId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterSpellEffectComponent");

            migrationBuilder.DropColumn(
                name: "Area",
                table: "PlayerCharacterSpell");
        }
    }
}
