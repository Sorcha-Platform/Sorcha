// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgIdentityManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserIdentities_ExternalIdpUserId",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.RenameColumn(
                name: "ExternalIdpUserId",
                schema: "public",
                table: "UserIdentities",
                newName: "ExternalIdpSubject");

            migrationBuilder.RenameColumn(
                name: "ProviderType",
                schema: "public",
                table: "IdentityProviderConfigurations",
                newName: "ProviderPreset");

            migrationBuilder.RenameIndex(
                name: "IX_IdentityProviderConfigurations_ProviderType",
                schema: "public",
                table: "IdentityProviderConfigurations",
                newName: "IX_IdentityProviderConfigurations_ProviderPreset");

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                schema: "public",
                table: "UserIdentities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAt",
                schema: "public",
                table: "UserIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                schema: "public",
                table: "UserIdentities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "InvitedByUserId",
                schema: "public",
                table: "UserIdentities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LockedPermanently",
                schema: "public",
                table: "UserIdentities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "public",
                table: "UserIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProfileCompleted",
                schema: "public",
                table: "UserIdentities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProvisionedVia",
                schema: "public",
                table: "UserIdentities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VerificationToken",
                schema: "public",
                table: "UserIdentities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerificationTokenExpiresAt",
                schema: "public",
                table: "UserIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "AllowedEmailDomains",
                schema: "public",
                table: "Organizations",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<int>(
                name: "AuditRetentionMonths",
                schema: "public",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CustomDomain",
                schema: "public",
                table: "Organizations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomDomainStatus",
                schema: "public",
                table: "Organizations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrgType",
                schema: "public",
                table: "Organizations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SelfRegistrationEnabled",
                schema: "public",
                table: "Organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DiscoveryDocumentJson",
                schema: "public",
                table: "IdentityProviderConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiscoveryFetchedAt",
                schema: "public",
                table: "IdentityProviderConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "public",
                table: "IdentityProviderConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                schema: "public",
                table: "IdentityProviderConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "JwksUri",
                schema: "public",
                table: "IdentityProviderConfigurations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserInfoEndpoint",
                schema: "public",
                table: "IdentityProviderConfigurations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomDomainMappings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomDomainMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgInvitations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AssignedRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgInvitations", x => x.Id);
                });

            // Data migration: consolidate deprecated roles (Developer, User, Consumer) to Member
            // The Roles column stores a comma-separated string of role values.
            // Replace any occurrence of "Developer", "User", or "Consumer" with "Member",
            // then deduplicate by converting to array and back.
            migrationBuilder.Sql("""
                UPDATE "public"."UserIdentities"
                SET "Roles" = REPLACE(REPLACE(REPLACE("Roles", 'Developer', 'Member'), 'User', 'Member'), 'Consumer', 'Member')
                WHERE "Roles" LIKE '%Developer%' OR "Roles" LIKE '%User%' OR "Roles" LIKE '%Consumer%';
                """);

            // Set default values for new columns on existing rows
            migrationBuilder.Sql("""
                UPDATE "public"."UserIdentities" SET "ProvisionedVia" = 'Local' WHERE "ProvisionedVia" = '';
                UPDATE "public"."UserIdentities" SET "ProfileCompleted" = true WHERE "ProfileCompleted" = false;
                UPDATE "public"."Organizations" SET "OrgType" = 'Standard' WHERE "OrgType" = '';
                UPDATE "public"."Organizations" SET "CustomDomainStatus" = 'None' WHERE "CustomDomainStatus" = '';
                UPDATE "public"."Organizations" SET "AuditRetentionMonths" = 12 WHERE "AuditRetentionMonths" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_ExternalIdpSubject",
                schema: "public",
                table: "UserIdentities",
                column: "ExternalIdpSubject",
                unique: true,
                filter: "\"ExternalIdpSubject\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomDomainMappings_Domain",
                schema: "public",
                table: "CustomDomainMappings",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomDomainMappings_OrganizationId",
                schema: "public",
                table: "CustomDomainMappings",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgInvitations_OrganizationId_Email_Status",
                schema: "public",
                table: "OrgInvitations",
                columns: new[] { "OrganizationId", "Email", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgInvitations_Token",
                schema: "public",
                table: "OrgInvitations",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomDomainMappings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrgInvitations",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_UserIdentities_ExternalIdpSubject",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "LockedPermanently",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "ProfileCompleted",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "ProvisionedVia",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "VerificationToken",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "VerificationTokenExpiresAt",
                schema: "public",
                table: "UserIdentities");

            migrationBuilder.DropColumn(
                name: "AllowedEmailDomains",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AuditRetentionMonths",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CustomDomain",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CustomDomainStatus",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "OrgType",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SelfRegistrationEnabled",
                schema: "public",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DiscoveryDocumentJson",
                schema: "public",
                table: "IdentityProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "DiscoveryFetchedAt",
                schema: "public",
                table: "IdentityProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "public",
                table: "IdentityProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                schema: "public",
                table: "IdentityProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "JwksUri",
                schema: "public",
                table: "IdentityProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "UserInfoEndpoint",
                schema: "public",
                table: "IdentityProviderConfigurations");

            migrationBuilder.RenameColumn(
                name: "ExternalIdpSubject",
                schema: "public",
                table: "UserIdentities",
                newName: "ExternalIdpUserId");

            migrationBuilder.RenameColumn(
                name: "ProviderPreset",
                schema: "public",
                table: "IdentityProviderConfigurations",
                newName: "ProviderType");

            migrationBuilder.RenameIndex(
                name: "IX_IdentityProviderConfigurations_ProviderPreset",
                schema: "public",
                table: "IdentityProviderConfigurations",
                newName: "IX_IdentityProviderConfigurations_ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_ExternalIdpUserId",
                schema: "public",
                table: "UserIdentities",
                column: "ExternalIdpUserId",
                unique: true,
                filter: "\"ExternalIdpUserId\" IS NOT NULL");
        }
    }
}
