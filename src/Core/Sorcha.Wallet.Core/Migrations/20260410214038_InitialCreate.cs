using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Wallet.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wallet");

            migrationBuilder.CreateTable(
                name: "Credentials",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IssuerDid = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SubjectDid = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ClaimsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawToken = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    IssuanceTxId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuanceBlueprintId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuanceInstanceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuanceActionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClaimActionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RegisterId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WalletAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UsagePolicy = table.Column<string>(type: "text", nullable: false),
                    MaxPresentations = table.Column<int>(type: "integer", nullable: true),
                    PresentationCount = table.Column<int>(type: "integer", nullable: false),
                    StatusListUrl = table.Column<string>(type: "text", nullable: true),
                    StatusListIndex = table.Column<int>(type: "integer", nullable: true),
                    DisplayConfigJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credentials", x => x.Id);
                });

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
                name: "RecoveryAuditLogs",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecoveryPath = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InitiatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    WalletsRecovered = table.Column<int>(type: "integer", nullable: false),
                    DelegationsRevoked = table.Column<int>(type: "integer", nullable: false),
                    DelegationsPreserved = table.Column<int>(type: "integer", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryAuditLogs", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "Wallets",
                schema: "wallet",
                columns: table => new
                {
                    Address = table.Column<string>(type: "text", nullable: false, comment: "Wallet address in Bech32m format (ws1...). Variable length by algorithm: ED25519 ~66 chars, NISTP256 ~107 chars, RSA4096 ~700 chars."),
                    EncryptedPrivateKey = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    EncryptionKeyId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tenant = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    Metadata = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    Tags = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Active"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EncryptedMasterKeyBlob = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: true),
                    RecoveryEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SigningMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Local"),
                    KmsKeyId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DerivedKeyRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustodyMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Custodial"),
                    HaipIssuer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Address);
                    table.ForeignKey(
                        name: "FK_Wallets_DerivedKeyRecords_DerivedKeyRecordId",
                        column: x => x.DerivedKeyRecordId,
                        principalSchema: "wallet",
                        principalTable: "DerivedKeyRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryKeyWraps",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    WalletAddress = table.Column<string>(type: "text", nullable: false),
                    RecoveryPath = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EncryptedRecoveryKey = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    RecipientKeyId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryKeyWraps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryKeyWraps_Wallets_WalletAddress",
                        column: x => x.WalletAddress,
                        principalSchema: "wallet",
                        principalTable: "Wallets",
                        principalColumn: "Address",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletAccess",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ParentWalletAddress = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AccessRight = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    GrantedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAccess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletAccess_Wallets_ParentWalletAddress",
                        column: x => x.ParentWalletAddress,
                        principalSchema: "wallet",
                        principalTable: "Wallets",
                        principalColumn: "Address",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletAddresses",
                schema: "wallet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ParentWalletAddress = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    DerivationPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    IsChange = table.Column<bool>(type: "boolean", nullable: false),
                    Label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    FirstUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Tags = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Metadata = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    Account = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletAddresses_Wallets_ParentWalletAddress",
                        column: x => x.ParentWalletAddress,
                        principalSchema: "wallet",
                        principalTable: "Wallets",
                        principalColumn: "Address",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                schema: "wallet",
                columns: table => new
                {
                    TransactionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ParentWalletAddress = table.Column<string>(type: "text", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    State = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    RawTransaction = table.Column<string>(type: "text", nullable: true),
                    RegisterId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReceiptId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReceiptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CounterpartyAddress = table.Column<string>(type: "text", nullable: true),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Outbound"),
                    Metadata = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_ParentWalletAddress",
                        column: x => x.ParentWalletAddress,
                        principalSchema: "wallet",
                        principalTable: "Wallets",
                        principalColumn: "Address",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_IssuerDid",
                schema: "wallet",
                table: "Credentials",
                column: "IssuerDid");

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_Status",
                schema: "wallet",
                table: "Credentials",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_Type",
                schema: "wallet",
                table: "Credentials",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_Wallet_Type",
                schema: "wallet",
                table: "Credentials",
                columns: new[] { "WalletAddress", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Credentials_WalletAddress",
                schema: "wallet",
                table: "Credentials",
                column: "WalletAddress");

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
                name: "IX_RecoveryAuditLogs_User_Timestamp",
                schema: "wallet",
                table: "RecoveryAuditLogs",
                columns: new[] { "UserId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryAuditLogs_UserId",
                schema: "wallet",
                table: "RecoveryAuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryKeyWraps_RecipientKeyId",
                schema: "wallet",
                table: "RecoveryKeyWraps",
                column: "RecipientKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryKeyWraps_Wallet_Path",
                schema: "wallet",
                table: "RecoveryKeyWraps",
                columns: new[] { "WalletAddress", "RecoveryPath" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryKeyWraps_WalletAddress",
                schema: "wallet",
                table: "RecoveryKeyWraps",
                column: "WalletAddress");

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

            migrationBuilder.CreateIndex(
                name: "IX_WalletAccess_Parent_Subject",
                schema: "wallet",
                table: "WalletAccess",
                columns: new[] { "ParentWalletAddress", "Subject" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAccess_ParentWalletAddress",
                schema: "wallet",
                table: "WalletAccess",
                column: "ParentWalletAddress");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAccess_Subject",
                schema: "wallet",
                table: "WalletAccess",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_WalletAccess_Subject_Active",
                schema: "wallet",
                table: "WalletAccess",
                columns: new[] { "Subject", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAddresses_Address",
                schema: "wallet",
                table: "WalletAddresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletAddresses_Derivation",
                schema: "wallet",
                table: "WalletAddresses",
                columns: new[] { "ParentWalletAddress", "Account", "IsChange", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAddresses_Parent_Index",
                schema: "wallet",
                table: "WalletAddresses",
                columns: new[] { "ParentWalletAddress", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAddresses_ParentWalletAddress",
                schema: "wallet",
                table: "WalletAddresses",
                column: "ParentWalletAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_DerivedKeyRecordId",
                schema: "wallet",
                table: "Wallets",
                column: "DerivedKeyRecordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Owner",
                schema: "wallet",
                table: "Wallets",
                column: "Owner");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Owner_Tenant",
                schema: "wallet",
                table: "Wallets",
                columns: new[] { "Owner", "Tenant" });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Status",
                schema: "wallet",
                table: "Wallets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Tenant",
                schema: "wallet",
                table: "Wallets",
                column: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_Tenant_Status",
                schema: "wallet",
                table: "Wallets",
                columns: new[] { "Tenant", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_CreatedAt",
                schema: "wallet",
                table: "WalletTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Parent_CreatedAt",
                schema: "wallet",
                table: "WalletTransactions",
                columns: new[] { "ParentWalletAddress", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Parent_State",
                schema: "wallet",
                table: "WalletTransactions",
                columns: new[] { "ParentWalletAddress", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ParentWalletAddress",
                schema: "wallet",
                table: "WalletTransactions",
                column: "ParentWalletAddress");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_State",
                schema: "wallet",
                table: "WalletTransactions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Credentials",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "RecoveryAuditLogs",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "RecoveryKeyWraps",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "SigningKeyShares",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "SigningSessions",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "WalletAccess",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "WalletAddresses",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "WalletTransactions",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "ThresholdKeyGroups",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "Wallets",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "DerivedKeyRecords",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "OrgMasterKeys",
                schema: "wallet");
        }
    }
}
