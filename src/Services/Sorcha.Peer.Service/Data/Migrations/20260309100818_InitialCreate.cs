// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Peer.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "peer");

            migrationBuilder.CreateTable(
                name: "Peers",
                schema: "peer",
                columns: table => new
                {
                    PeerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Protocols = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FirstSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsSeedNode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AverageLatencyMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsBanned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    BannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BanReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Peers", x => x.PeerId);
                });

            migrationBuilder.CreateTable(
                name: "queued_transactions",
                schema: "peer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OriginPeerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DataSize = table.Column<int>(type: "integer", nullable: false),
                    DataHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GossipRound = table.Column<int>(type: "integer", nullable: false),
                    HopCount = table.Column<int>(type: "integer", nullable: false),
                    TTL = table.Column<int>(type: "integer", nullable: false, defaultValue: 3600),
                    HasFullData = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TransactionData = table.Column<byte[]>(type: "bytea", nullable: true),
                    EnqueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queued_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegisterSubscriptions",
                schema: "peer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    SyncState = table.Column<int>(type: "integer", nullable: false),
                    LastSyncedDocketVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncedTransactionVersion = table.Column<long>(type: "bigint", nullable: false),
                    TotalDocketsInChain = table.Column<long>(type: "bigint", nullable: false),
                    SourcePeerIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisterSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncCheckpoints",
                schema: "peer",
                columns: table => new
                {
                    PeerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CurrentVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastSyncTime = table.Column<long>(type: "bigint", nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    SourcePeerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NextSyncDue = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCheckpoints", x => new { x.PeerId, x.RegisterId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Peers_IsBanned",
                schema: "peer",
                table: "Peers",
                column: "IsBanned");

            migrationBuilder.CreateIndex(
                name: "IX_Peers_IsSeedNode",
                schema: "peer",
                table: "Peers",
                column: "IsSeedNode");

            migrationBuilder.CreateIndex(
                name: "IX_Peers_LastSeen",
                schema: "peer",
                table: "Peers",
                column: "LastSeen");

            migrationBuilder.CreateIndex(
                name: "IX_QueuedTransactions_EnqueuedAt",
                schema: "peer",
                table: "queued_transactions",
                column: "EnqueuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_QueuedTransactions_RegisterId",
                schema: "peer",
                table: "queued_transactions",
                column: "RegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_QueuedTransactions_Status",
                schema: "peer",
                table: "queued_transactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RegisterSubscriptions_RegisterId",
                schema: "peer",
                table: "RegisterSubscriptions",
                column: "RegisterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisterSubscriptions_SyncState",
                schema: "peer",
                table: "RegisterSubscriptions",
                column: "SyncState");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Peers",
                schema: "peer");

            migrationBuilder.DropTable(
                name: "queued_transactions",
                schema: "peer");

            migrationBuilder.DropTable(
                name: "RegisterSubscriptions",
                schema: "peer");

            migrationBuilder.DropTable(
                name: "SyncCheckpoints",
                schema: "peer");
        }
    }
}
