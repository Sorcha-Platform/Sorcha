using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Peer.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuedTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queued_transactions",
                schema: "peer");
        }
    }
}
