using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeWeaponDamage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalAttackBonus",
                table: "PlayerCharacterWeapon");

            migrationBuilder.DropColumn(
                name: "AdditionalDamageBonus",
                table: "PlayerCharacterWeapon");

            migrationBuilder.DropColumn(
                name: "DamageDice",
                table: "PlayerCharacterWeapon");

            migrationBuilder.DropColumn(
                name: "DamageType",
                table: "PlayerCharacterWeapon");

            migrationBuilder.DropColumn(
                name: "VersatileDamageDice",
                table: "PlayerCharacterWeapon");

            migrationBuilder.RenameColumn(
                name: "MagicBonus",
                table: "PlayerCharacterWeapon",
                newName: "PrimaryDamageBonus");

            migrationBuilder.RenameColumn(
                name: "AddsAbilityModifierToDamage",
                table: "PlayerCharacterWeapon",
                newName: "AttackBonus");

            migrationBuilder.CreateTable(
                name: "PlayerCharacterWeaponDamageComponent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DamageDice = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DamageType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddsAbilityModifier = table.Column<bool>(type: "INTEGER", nullable: false),
                    VersatileDamageDice = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterWeaponDamageComponent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterWeaponDamageComponent_PlayerCharacterWeapon_PlayerCharacterItemId",
                        column: x => x.PlayerCharacterItemId,
                        principalTable: "PlayerCharacterWeapon",
                        principalColumn: "PlayerCharacterItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterWeaponDamageComponent_PlayerCharacterItemId_SortOrder",
                table: "PlayerCharacterWeaponDamageComponent",
                columns: new[] { "PlayerCharacterItemId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterWeaponDamageComponent");

            migrationBuilder.RenameColumn(
                name: "PrimaryDamageBonus",
                table: "PlayerCharacterWeapon",
                newName: "MagicBonus");

            migrationBuilder.RenameColumn(
                name: "AttackBonus",
                table: "PlayerCharacterWeapon",
                newName: "AddsAbilityModifierToDamage");

            migrationBuilder.AddColumn<int>(
                name: "AdditionalAttackBonus",
                table: "PlayerCharacterWeapon",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AdditionalDamageBonus",
                table: "PlayerCharacterWeapon",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DamageDice",
                table: "PlayerCharacterWeapon",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DamageType",
                table: "PlayerCharacterWeapon",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VersatileDamageDice",
                table: "PlayerCharacterWeapon",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }
    }
}
