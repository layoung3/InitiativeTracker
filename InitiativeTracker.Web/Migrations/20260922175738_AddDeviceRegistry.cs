using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevicePairingRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PairingCode = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedDeviceId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevicePairingRequest", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegisteredDevice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DeviceType = table.Column<string>(type: "TEXT", nullable: false),
                    IsTrusted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PairedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredDevice", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevicePairingRequest");

            migrationBuilder.DropTable(
                name: "RegisteredDevice");
        }
    }
}
