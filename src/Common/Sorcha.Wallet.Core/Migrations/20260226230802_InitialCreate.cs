// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
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
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Address);
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
                name: "WalletAccess",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "WalletAddresses",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "WalletTransactions",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "Wallets",
                schema: "wallet");
        }
    }
}
