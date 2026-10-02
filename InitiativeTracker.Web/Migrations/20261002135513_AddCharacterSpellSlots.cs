using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterSpellSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerCharacterSpellSlot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerCharacterSpellcastingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    MaximumSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    RemainingSlots = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacterSpellSlot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerCharacterSpellSlot_PlayerCharacterSpellcasting_PlayerCharacterSpellcastingId",
                        column: x => x.PlayerCharacterSpellcastingId,
                        principalTable: "PlayerCharacterSpellcasting",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacterSpellSlot_PlayerCharacterSpellcastingId_Level",
                table: "PlayerCharacterSpellSlot",
                columns: new[] { "PlayerCharacterSpellcastingId", "Level" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacterSpellSlot");
        }
    }
}
