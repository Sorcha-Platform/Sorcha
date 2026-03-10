using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sorcha.Tenant.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SourceService = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Details = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

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
                name: "OrganizationPermissionConfigurations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedBlockchains = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    CanCreateBlockchain = table.Column<bool>(type: "boolean", nullable: false),
                    CanPublishBlueprint = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationPermissionConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subdomain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatorIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrgType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SelfRegistrationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedEmailDomains = table.Column<string[]>(type: "text[]", nullable: false),
                    CustomDomain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CustomDomainStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuditRetentionMonths = table.Column<int>(type: "integer", nullable: false),
                    Branding = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "ParticipantIdentities",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasskeyCredentials",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", nullable: false),
                    PublicKeyCose = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttestationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AaGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasskeyCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicIdentities",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceType = table.Column<string>(type: "text", nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    P256dhKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AuthKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServicePrincipals",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClientSecretEncrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    Scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePrincipals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigurations",
                schema: "public",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigurations", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TotpConfigurations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BackupCodes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotpConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentities",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalIdpSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Roles = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VerificationTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProvisionedVia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedPermanently = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Theme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    TimeFormat = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DefaultWalletAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    NotificationMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "InApp"),
                    NotificationFrequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "RealTime"),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityProviderConfigurations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderPreset = table.Column<string>(type: "text", nullable: false),
                    IssuerUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientSecretEncrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    Scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    AuthorizationEndpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TokenEndpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UserInfoEndpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    JwksUri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MetadataUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DiscoveryDocumentJson = table.Column<string>(type: "text", nullable: true),
                    DiscoveryFetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityProviderConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdentityProviderConfigurations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "public",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkedWalletAddresses",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedWalletAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkedWalletAddresses_ParticipantIdentities_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "public",
                        principalTable: "ParticipantIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParticipantAuditEntries",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OldValues = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    NewValues = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParticipantAuditEntries_ParticipantIdentities_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "public",
                        principalTable: "ParticipantIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletLinkChallenges",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Challenge = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletLinkChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletLinkChallenges_ParticipantIdentities_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "public",
                        principalTable: "ParticipantIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialLoginLinks",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicIdentityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalSubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LinkedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialLoginLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialLoginLinks_PublicIdentities_PublicIdentityId",
                        column: x => x.PublicIdentityId,
                        principalSchema: "public",
                        principalTable: "PublicIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvent_ExpiresAt",
                schema: "public",
                table: "ActivityEvents",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvent_OrgId_CreatedAt",
                schema: "public",
                table: "ActivityEvents",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvent_UserId_CreatedAt",
                schema: "public",
                table: "ActivityEvents",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvent_UserId_IsRead",
                schema: "public",
                table: "ActivityEvents",
                columns: new[] { "UserId", "IsRead" },
                filter: "\"IsRead\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_EventType",
                schema: "public",
                table: "AuditLogEntries",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_IdentityId",
                schema: "public",
                table: "AuditLogEntries",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_OrganizationId",
                schema: "public",
                table: "AuditLogEntries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Timestamp",
                schema: "public",
                table: "AuditLogEntries",
                column: "Timestamp");

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
                name: "IX_IdentityProviderConfigurations_OrganizationId",
                schema: "public",
                table: "IdentityProviderConfigurations",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityProviderConfigurations_ProviderPreset",
                schema: "public",
                table: "IdentityProviderConfigurations",
                column: "ProviderPreset");

            migrationBuilder.CreateIndex(
                name: "IX_WalletLink_Address",
                schema: "public",
                table: "LinkedWalletAddresses",
                column: "WalletAddress",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_WalletLink_Participant",
                schema: "public",
                table: "LinkedWalletAddresses",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPermissionConfigurations_OrganizationId",
                schema: "public",
                table: "OrganizationPermissionConfigurations",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Status",
                schema: "public",
                table: "Organizations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Subdomain",
                schema: "public",
                table: "Organizations",
                column: "Subdomain",
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

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Actor_Time",
                schema: "public",
                table: "ParticipantAuditEntries",
                columns: new[] { "ActorId", "Timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Participant_Time",
                schema: "public",
                table: "ParticipantAuditEntries",
                columns: new[] { "ParticipantId", "Timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Participant_Org_Status",
                schema: "public",
                table: "ParticipantIdentities",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantIdentities_UserId",
                schema: "public",
                table: "ParticipantIdentities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_Participant_User_Org",
                schema: "public",
                table: "ParticipantIdentities",
                columns: new[] { "UserId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredential_OrgId",
                schema: "public",
                table: "PasskeyCredentials",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredential_Owner",
                schema: "public",
                table: "PasskeyCredentials",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredential_OwnerId_Status",
                schema: "public",
                table: "PasskeyCredentials",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredentials_CredentialId",
                schema: "public",
                table: "PasskeyCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicIdentities_Email",
                schema: "public",
                table: "PublicIdentities",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscription_UserId",
                schema: "public",
                table: "PushSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_PushSubscription_User_Endpoint",
                schema: "public",
                table: "PushSubscriptions",
                columns: new[] { "UserId", "Endpoint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePrincipals_ClientId",
                schema: "public",
                table: "ServicePrincipals",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePrincipals_ServiceName",
                schema: "public",
                table: "ServicePrincipals",
                column: "ServiceName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialLogin_PublicIdentityId",
                schema: "public",
                table: "SocialLoginLinks",
                column: "PublicIdentityId");

            migrationBuilder.CreateIndex(
                name: "UQ_SocialLogin_Provider_Subject",
                schema: "public",
                table: "SocialLoginLinks",
                columns: new[] { "ProviderType", "ExternalSubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TotpConfiguration_UserId",
                schema: "public",
                table: "TotpConfigurations",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_Email",
                schema: "public",
                table: "UserIdentities",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_ExternalIdpSubject",
                schema: "public",
                table: "UserIdentities",
                column: "ExternalIdpSubject",
                unique: true,
                filter: "\"ExternalIdpSubject\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_OrganizationId",
                schema: "public",
                table: "UserIdentities",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_Status",
                schema: "public",
                table: "UserIdentities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UQ_UserPreferences_UserId",
                schema: "public",
                table: "UserPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Challenge_Address_Status",
                schema: "public",
                table: "WalletLinkChallenges",
                columns: new[] { "WalletAddress", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Challenge_Participant_Status",
                schema: "public",
                table: "WalletLinkChallenges",
                columns: new[] { "ParticipantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "AuditLogEntries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CustomDomainMappings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "IdentityProviderConfigurations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "LinkedWalletAddresses",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrganizationPermissionConfigurations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrgInvitations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ParticipantAuditEntries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PasskeyCredentials",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PushSubscriptions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServicePrincipals",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SocialLoginLinks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SystemConfigurations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TotpConfigurations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "UserIdentities",
                schema: "public");

            migrationBuilder.DropTable(
                name: "UserPreferences",
                schema: "public");

            migrationBuilder.DropTable(
                name: "WalletLinkChallenges",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PublicIdentities",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ParticipantIdentities",
                schema: "public");
        }
    }
}
