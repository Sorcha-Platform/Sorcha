using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformUserDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformUserDevices",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DevicePublicJwkThumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DevicePublicJwkJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByPlatformUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DelegationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DelegationCredentialJti = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusListId = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StatusListIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUserDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformUserDevices_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserDevices_DevicePublicJwkThumbprint",
                schema: "public",
                table: "PlatformUserDevices",
                column: "DevicePublicJwkThumbprint");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserDevices_PlatformUserId_Status",
                schema: "public",
                table: "PlatformUserDevices",
                columns: new[] { "PlatformUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserDevices_StatusListIndex",
                schema: "public",
                table: "PlatformUserDevices",
                column: "StatusListIndex");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformUserDevices",
                schema: "public");
        }
    }
}
