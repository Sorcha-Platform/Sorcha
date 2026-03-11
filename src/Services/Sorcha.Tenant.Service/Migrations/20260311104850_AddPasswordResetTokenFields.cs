using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetTokenFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordResetTokenExpiresAt",
                schema: "public",
                table: "UserIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                schema: "public",
                table: "UserIdentities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentity_PasswordResetTokenHash",
                schema: "public",
                table: "UserIdentities",
                column: "PasswordResetTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserIdentity_PasswordResetTokenHash",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiresAt",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                schema: "public",
                table: "UserIdentities");
        }
    }
}
