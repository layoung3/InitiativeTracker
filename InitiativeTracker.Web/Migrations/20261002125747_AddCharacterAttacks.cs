using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterAttacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerCharacterAttack",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    AttackAbility = table.Column<int>(type: "INTEGER", nullable: false),
                    AddsAbilityModifierToHit = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsesProficiency = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttackBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    NormalRange = table.Column<int>(type: "INTEGER", nullable: true),
                    LongRange = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterAttack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterAttack_PlayerCharacter_PlayerCharacterId",
                        column: x => x.PlayerCharacterId,
                        principalTable: "PlayerCharacter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerCharacterAttackDamageComponent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterAttackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DamageExpression = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DamageType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AddsAbilityModifier = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdditionalDamageBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterAttackDamageComponent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterAttackDamageComponent_PlayerCharacterAttack_PlayerCharacterAttackId",
                        column: x => x.PlayerCharacterAttackId,
                        principalTable: "PlayerCharacterAttack",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterAttack_PlayerCharacterId_SortOrder",
                table: "PlayerCharacterAttack",
                columns: new[] { "PlayerCharacterId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterAttackDamageComponent_PlayerCharacterAttackId_SortOrder",
                table: "PlayerCharacterAttackDamageComponent",
                columns: new[] { "PlayerCharacterAttackId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterAttackDamageComponent");

            migrationBuilder.DropTable(
                name: "PlayerCharacterAttack");
        }
    }
}
