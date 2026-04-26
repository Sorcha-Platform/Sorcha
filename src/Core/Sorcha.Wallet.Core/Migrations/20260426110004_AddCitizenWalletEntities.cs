using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Wallet.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCitizenWalletEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CitizenDeviceStatusLists",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListId = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    Bitstring = table.Column<byte[]>(type: "bytea", nullable: false),
                    RevokedCount = table.Column<int>(type: "integer", nullable: false),
                    LastAllocatedIndex = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SignedJwt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitizenDeviceStatusLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CitizenWalletSyncCursors",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastEventSeq = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CitizenWalletSyncCursors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenDeviceStatusLists_Org_ListId",
                schema: "wallet",
                table: "CitizenDeviceStatusLists",
                columns: new[] { "OrganizationId", "ListId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CitizenWalletSyncCursors_User_Device",
                schema: "wallet",
                table: "CitizenWalletSyncCursors",
                columns: new[] { "PlatformUserId", "PlatformUserDeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitizenDeviceStatusLists",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "CitizenWalletSyncCursors",
                schema: "wallet");
        }
    }
}
