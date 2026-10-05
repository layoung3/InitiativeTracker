using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class HardenPlayerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AccessToken",
                table: "PlayerProfile",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAtUtc",
                table: "PlayerProfile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryKeyHash",
                table: "PlayerProfile",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerDeviceCredentialId",
                table: "PlayerConnection",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerDeviceCredential",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDeviceCredential", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerDeviceCredential_PlayerProfile_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerProfile_RecoveryKeyHash",
                table: "PlayerProfile",
                column: "RecoveryKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerConnection_PlayerDeviceCredentialId",
                table: "PlayerConnection",
                column: "PlayerDeviceCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerConnection_PlayerProfileId",
                table: "PlayerConnection",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerConnection_SessionId_IsActive_LastSeenAtUtc",
                table: "PlayerConnection",
                columns: new[] { "SessionId", "IsActive", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDeviceCredential_PlayerProfileId",
                table: "PlayerDeviceCredential",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDeviceCredential_TokenHash",
                table: "PlayerDeviceCredential",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerConnection_PlayerDeviceCredential_PlayerDeviceCredentialId",
                table: "PlayerConnection",
                column: "PlayerDeviceCredentialId",
                principalTable: "PlayerDeviceCredential",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerConnection_PlayerDeviceCredential_PlayerDeviceCredentialId",
                table: "PlayerConnection");

            migrationBuilder.DropTable(
                name: "PlayerDeviceCredential");

            migrationBuilder.DropIndex(
                name: "IX_PlayerProfile_RecoveryKeyHash",
                table: "PlayerProfile");

            migrationBuilder.DropIndex(
                name: "IX_PlayerConnection_PlayerDeviceCredentialId",
                table: "PlayerConnection");

            migrationBuilder.DropIndex(
                name: "IX_PlayerConnection_PlayerProfileId",
                table: "PlayerConnection");

            migrationBuilder.DropIndex(
                name: "IX_PlayerConnection_SessionId_IsActive_LastSeenAtUtc",
                table: "PlayerConnection");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "PlayerProfile");

            migrationBuilder.DropColumn(
                name: "RecoveryKeyHash",
                table: "PlayerProfile");

            migrationBuilder.DropColumn(
                name: "PlayerDeviceCredentialId",
                table: "PlayerConnection");

            migrationBuilder.AlterColumn<string>(
                name: "AccessToken",
                table: "PlayerProfile",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
