using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterWeapons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerCharacterWeapon",
                columns: table => new
                {
                    PlayerCharacterItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DamageDice = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DamageType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AttackAbility = table.Column<int>(type: "INTEGER", nullable: false),
                    UsesProficiency = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddsAbilityModifierToDamage = table.Column<bool>(type: "INTEGER", nullable: false),
                    MagicBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalAttackBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalDamageBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    VersatileDamageDice = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    NormalRange = table.Column<int>(type: "INTEGER", nullable: true),
                    LongRange = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterWeapon", x => x.PlayerCharacterItemId);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterWeapon_PlayerCharacterItem_PlayerCharacterItemId",
                        column: x => x.PlayerCharacterItemId,
                        principalTable: "PlayerCharacterItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterWeapon");
        }
    }
}
