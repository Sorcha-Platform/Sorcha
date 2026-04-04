using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Wallet.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgKeyDerivationAndThresholdSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustodyMode",
                schema: "wallet",
                table: "Wallets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Custodial");

            migrationBuilder.AddColumn<Guid>(
                name: "DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrgMasterKeys",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EncryptedSeed = table.Column<byte[]>(type: "bytea", nullable: false),
                    ProtectionProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProtectionKeyId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "ED25519"),
                    MasterPublicKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    RotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMasterKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThresholdKeyGroups",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    GroupPublicKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    TotalShares = table.Column<int>(type: "integer", nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DkgSessionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OrganizationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThresholdKeyGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DerivedKeyRecords",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrgMasterKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    KeyUsage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    KeyIndex = table.Column<long>(type: "bigint", nullable: false),
                    DerivationPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    WalletAddress = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CustodyMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Custodial"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DerivedKeyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DerivedKeyRecords_OrgMasterKeys_OrgMasterKeyId",
                        column: x => x.OrgMasterKeyId,
                        principalSchema: "wallet",
                        principalTable: "OrgMasterKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SigningKeyShares",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ThresholdKeyGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ShareIndex = table.Column<int>(type: "integer", nullable: false),
                    EncryptedShareData = table.Column<byte[]>(type: "bytea", nullable: false),
                    ProtectionKeyId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningKeyShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SigningKeyShares_ThresholdKeyGroups_ThresholdKeyGroupId",
                        column: x => x.ThresholdKeyGroupId,
                        principalSchema: "wallet",
                        principalTable: "ThresholdKeyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SigningSessions",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ThresholdKeyGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Initializing"),
                    RequiredSigners = table.Column<int>(type: "integer", nullable: false),
                    CollectedPartials = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SigningSessions_ThresholdKeyGroups_ThresholdKeyGroupId",
                        column: x => x.ThresholdKeyGroupId,
                        principalSchema: "wallet",
                        principalTable: "ThresholdKeyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets",
                column: "DerivedKeyRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DerivedKeyRecords_Org_User",
                schema: "wallet",
                table: "DerivedKeyRecords",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DerivedKeyRecords_Unique_Path",
                schema: "wallet",
                table: "DerivedKeyRecords",
                columns: new[] { "OrgMasterKeyId", "UserId", "DepartmentId", "KeyUsage", "KeyIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DerivedKeyRecords_WalletAddress",
                schema: "wallet",
                table: "DerivedKeyRecords",
                column: "WalletAddress");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMasterKeys_OrganizationId",
                schema: "wallet",
                table: "OrgMasterKeys",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningKeyShares_Group_Index",
                schema: "wallet",
                table: "SigningKeyShares",
                columns: new[] { "ThresholdKeyGroupId", "ShareIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningSessions_ThresholdKeyGroupId",
                schema: "wallet",
                table: "SigningSessions",
                column: "ThresholdKeyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ThresholdKeyGroups_OrganizationId",
                schema: "wallet",
                table: "ThresholdKeyGroups",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Wallets_DerivedKeyRecords_DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets",
                column: "DerivedKeyRecordId",
                principalSchema: "wallet",
                principalTable: "DerivedKeyRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Wallets_DerivedKeyRecords_DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets");

            migrationBuilder.DropTable(
                name: "DerivedKeyRecords",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "SigningKeyShares",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "SigningSessions",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "OrgMasterKeys",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "ThresholdKeyGroups",
                schema: "wallet");

            migrationBuilder.DropIndex(
                name: "IX_Wallets_DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "CustodyMode",
                schema: "wallet",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets");
        }
    }
}
