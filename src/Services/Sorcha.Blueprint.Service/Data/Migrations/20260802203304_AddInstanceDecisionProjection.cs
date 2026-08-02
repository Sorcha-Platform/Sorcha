using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Blueprint.Service.Data.Migrations
{
    /// <summary>
    /// Feature 186 (#1163) — adds the projected decision (<c>DecisionRouteId</c>,
    /// <c>DecisionReasonCode</c>) to <c>Instances</c>.
    /// </summary>
    /// <remarks>
    /// A separate migration on purpose, rather than amending <c>InitialCreate</c> in place as
    /// <c>LastAppliedTxId</c> was. Amending works only on a <b>fresh</b> database: an existing one
    /// already has <c>InitialCreate</c> recorded in <c>__EFMigrationsHistory</c>, so
    /// <c>MigrateAsync</c> does nothing and the columns are never created. This was not theoretical
    /// — the first live call against a running stack returned
    /// <c>42703: column i.DecisionReasonCode does not exist</c>.
    /// </remarks>
    public partial class AddInstanceDecisionProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionRouteId",
                schema: "blueprint",
                table: "Instances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionReasonCode",
                schema: "blueprint",
                table: "Instances",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecisionReasonCode",
                schema: "blueprint",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "DecisionRouteId",
                schema: "blueprint",
                table: "Instances");
        }
    }
}
