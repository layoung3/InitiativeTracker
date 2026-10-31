using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeTracker.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPresentationAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PresentationAssets",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BlobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresentationAssets", x => new { x.Scope, x.SubjectId, x.Kind });
                });

            migrationBuilder.CreateTable(
                name: "PresentationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ShowApplicationLogo = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresentationSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PresentationAssets_BlobId",
                table: "PresentationAssets",
                column: "BlobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PresentationAssets_OwnerProfileId",
                table: "PresentationAssets",
                column: "OwnerProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PresentationAssets");

            migrationBuilder.DropTable(
                name: "PresentationSettings");
        }
    }
}
