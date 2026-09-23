using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerProfilesAndCharacters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlayerProfileId",
                table: "PlayerConnection",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "PlayerCharacter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    ProficiencyBonus = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentHp = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxHp = table.Column<int>(type: "INTEGER", nullable: false),
                    Inspiration = table.Column<bool>(type: "INTEGER", nullable: false),
                    Strength = table.Column<int>(type: "INTEGER", nullable: false),
                    Dexterity = table.Column<int>(type: "INTEGER", nullable: false),
                    Constitution = table.Column<int>(type: "INTEGER", nullable: false),
                    Intelligence = table.Column<int>(type: "INTEGER", nullable: false),
                    Wisdom = table.Column<int>(type: "INTEGER", nullable: false),
                    Charisma = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCharacter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerProfile", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCharacter_PlayerProfileId",
                table: "PlayerCharacter",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfile_AccessToken",
                table: "PlayerProfile",
                column: "AccessToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCharacter");

            migrationBuilder.DropTable(
                name: "PlayerProfile");

            migrationBuilder.DropColumn(
                name: "PlayerProfileId",
                table: "PlayerConnection");
        }
    }
}
