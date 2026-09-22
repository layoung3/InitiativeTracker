using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameSession",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JoinCode = table.Column<string>(type: "TEXT", nullable: false),
                    DmPin = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerJoinToken = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayToken = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSession", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSession");
        }
    }
}
