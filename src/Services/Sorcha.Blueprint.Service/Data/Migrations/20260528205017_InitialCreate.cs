using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Blueprint.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "blueprint");

            migrationBuilder.CreateTable(
                name: "Actions",
                schema: "blueprint",
                columns: table => new
                {
                    TransactionHash = table.Column<string>(type: "text", nullable: false),
                    WalletAddress = table.Column<string>(type: "text", nullable: false),
                    RegisterAddress = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true),
                    IdempotencyExpiry = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Actions", x => x.TransactionHash);
                });

            migrationBuilder.CreateTable(
                name: "BlueprintDrafts",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlueprintDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlueprintTemplates",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlueprintTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instances",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BlueprintId = table.Column<string>(type: "text", nullable: false),
                    BlueprintVersion = table.Column<int>(type: "integer", nullable: false),
                    RegisterId = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CurrentActionIds = table.Column<string>(type: "jsonb", nullable: true),
                    ParticipantWallets = table.Column<string>(type: "jsonb", nullable: true),
                    FirstTransactionId = table.Column<string>(type: "text", nullable: true),
                    LastTransactionId = table.Column<string>(type: "text", nullable: true),
                    CompletedActionCount = table.Column<int>(type: "integer", nullable: false),
                    AccumulatedData = table.Column<string>(type: "jsonb", nullable: true),
                    PendingActionPayloads = table.Column<string>(type: "jsonb", nullable: true),
                    ActiveBranches = table.Column<string>(type: "jsonb", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAppliedTxId = table.Column<string>(type: "text", nullable: true),
                    DecisionRouteId = table.Column<string>(type: "text", nullable: true),
                    DecisionReasonCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublishOverrides",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlueprintId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RegisterId = table.Column<string>(type: "text", nullable: false),
                    ExecDefHash = table.Column<string>(type: "text", nullable: false),
                    OverriddenByPlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OverriddenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RehearsalPasses",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlueprintId = table.Column<string>(type: "text", nullable: false),
                    ExecDefHash = table.Column<string>(type: "text", nullable: false),
                    RehearsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RehearsedByPlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SandboxRegisterId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RehearsalPasses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatusLists",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    IssuerWallet = table.Column<string>(type: "text", nullable: false),
                    RegisterId = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    EncodedList = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<int>(type: "integer", nullable: false),
                    NextAvailableIndex = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReconciledToDocket = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileMetadata",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    TransactionHash = table.Column<string>(type: "text", nullable: true),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CustomMetadata = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileMetadata_Actions_TransactionHash",
                        column: x => x.TransactionHash,
                        principalSchema: "blueprint",
                        principalTable: "Actions",
                        principalColumn: "TransactionHash",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlueprintDraftAccess",
                schema: "blueprint",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DraftId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AccessLevel = table.Column<string>(type: "text", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlueprintDraftAccess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlueprintDraftAccess_BlueprintDrafts_DraftId",
                        column: x => x.DraftId,
                        principalSchema: "blueprint",
                        principalTable: "BlueprintDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatusLists_IssuerWallet_RegisterId_Purpose",
                schema: "blueprint",
                table: "StatusLists",
                columns: new[] { "IssuerWallet", "RegisterId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_Orphans",
                schema: "blueprint",
                table: "FileMetadata",
                column: "CreatedAt",
                filter: "\"TransactionHash\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Actions_Wallet_Register_CreatedAt",
                schema: "blueprint",
                table: "Actions",
                columns: new[] { "WalletAddress", "RegisterAddress", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_Actions_IdempotencyKey",
                schema: "blueprint",
                table: "Actions",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlueprintDraftAccess_DraftId_UserId",
                schema: "blueprint",
                table: "BlueprintDraftAccess",
                columns: new[] { "DraftId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_OrgId",
                schema: "blueprint",
                table: "BlueprintDrafts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Drafts_OwnerId",
                schema: "blueprint",
                table: "BlueprintDrafts",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Category",
                schema: "blueprint",
                table: "BlueprintTemplates",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_TransactionHash",
                schema: "blueprint",
                table: "FileMetadata",
                column: "TransactionHash");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_BlueprintId_CreatedAt",
                schema: "blueprint",
                table: "Instances",
                columns: new[] { "BlueprintId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_RegisterId_CreatedAt",
                schema: "blueprint",
                table: "Instances",
                columns: new[] { "RegisterId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Instances_State_UpdatedAt",
                schema: "blueprint",
                table: "Instances",
                columns: new[] { "State", "UpdatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PublishOverrides_Blueprint_OverriddenAt",
                schema: "blueprint",
                table: "PublishOverrides",
                columns: new[] { "BlueprintId", "OverriddenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RehearsalPasses_Blueprint_ExecDef_RehearsedAt",
                schema: "blueprint",
                table: "RehearsalPasses",
                columns: new[] { "BlueprintId", "ExecDefHash", "RehearsedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlueprintDraftAccess",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "BlueprintTemplates",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "StatusLists",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "FileMetadata",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "Instances",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "PublishOverrides",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "RehearsalPasses",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "BlueprintDrafts",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "Actions",
                schema: "blueprint");
        }
    }
}
