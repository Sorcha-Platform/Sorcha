using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Wallet.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCitizenPresentationRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CitizenPresentationRecords",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifierLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VerifierDid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisclosedClaims = table.Column<string>(type: "jsonb", nullable: false),
                    PresentedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitizenPresentationRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenPresentationRecords_User_Entry",
                schema: "wallet",
                table: "CitizenPresentationRecords",
                columns: new[] { "PlatformUserId", "EntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CitizenPresentationRecords_User_PresentedAt",
                schema: "wallet",
                table: "CitizenPresentationRecords",
                columns: new[] { "PlatformUserId", "PresentedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitizenPresentationRecords",
                schema: "wallet");
        }
    }
}
