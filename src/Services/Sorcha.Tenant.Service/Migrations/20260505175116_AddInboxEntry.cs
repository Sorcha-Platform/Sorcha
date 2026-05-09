using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxEntries",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DetailHref = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IconKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChannelHints = table.Column<int>(type: "integer", nullable: false),
                    WriterServiceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxEntries_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_Category_OccurredAt",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_CorrelationKey_OccurredAt",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "CorrelationKey", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_OccurredAt",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_SourceEventId",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "SourceEventId" },
                unique: true);

            // Feature 120 US2 — published per-org DID documents.
            // Squashed into AddInboxEntry per the preproduction migration-squash rule:
            // no new top-level migration files added for F120's persistence layer.
            migrationBuilder.CreateTable(
                name: "OrgDidDocuments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryDid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FederatedDid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    KeyVersionFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastRegeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRegenerationReason = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgDidDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgDidDocuments_FederatedDid",
                schema: "public",
                table: "OrgDidDocuments",
                column: "FederatedDid");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDidDocuments_OrganizationId",
                schema: "public",
                table: "OrgDidDocuments",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgDidDocuments_PrimaryDid",
                schema: "public",
                table: "OrgDidDocuments",
                column: "PrimaryDid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgDidDocuments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "InboxEntries",
                schema: "public");
        }
    }
}
