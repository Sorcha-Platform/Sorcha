using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationFrequency",
                schema: "public",
                table: "UserPreferences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "RealTime");

            migrationBuilder.AddColumn<string>(
                name: "NotificationMethod",
                schema: "public",
                table: "UserPreferences",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "InApp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationFrequency",
                schema: "public",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "NotificationMethod",
                schema: "public",
                table: "UserPreferences");
        }
    }
}
