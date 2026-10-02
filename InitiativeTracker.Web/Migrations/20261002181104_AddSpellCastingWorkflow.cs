using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSpellCastingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CantripScalingExpression",
                table: "PlayerCharacterSpellEffectComponent",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveConcentrationSpellId",
                table: "PlayerCharacterSpellcasting",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CantripScalingExpression",
                table: "PlayerCharacterSpellEffectComponent");

            migrationBuilder.DropColumn(
                name: "ActiveConcentrationSpellId",
                table: "PlayerCharacterSpellcasting");
        }
    }
}
