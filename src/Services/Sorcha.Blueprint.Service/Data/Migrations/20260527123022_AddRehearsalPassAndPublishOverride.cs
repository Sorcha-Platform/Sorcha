using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Blueprint.Service.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRehearsalPassAndPublishOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "PublishOverrides",
                schema: "blueprint");

            migrationBuilder.DropTable(
                name: "RehearsalPasses",
                schema: "blueprint");
        }
    }
}
