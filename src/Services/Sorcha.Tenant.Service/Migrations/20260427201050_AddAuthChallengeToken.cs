using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthChallengeToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthChallengeTokens",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScopedOperation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthChallengeTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthChallengeTokens_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthChallengeToken_ExpiresAt",
                schema: "public",
                table: "AuthChallengeTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthChallengeToken_User_Active",
                schema: "public",
                table: "AuthChallengeTokens",
                columns: new[] { "PlatformUserId", "ConsumedAt" },
                filter: "\"ConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_AuthChallengeToken_TokenHash",
                schema: "public",
                table: "AuthChallengeTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthChallengeTokens",
                schema: "public");
        }
    }
}
