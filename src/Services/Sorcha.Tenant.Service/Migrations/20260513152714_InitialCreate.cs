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
                name: "InvitationNonces",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvitationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceOrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetOrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationNonces", x => x.Id);
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
                    IsPlatformOrg = table.Column<bool>(type: "boolean", nullable: false),
                    OrgType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SelfRegistrationEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedEmailDomains = table.Column<string[]>(type: "text[]", nullable: false),
                    CustomDomain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CustomDomainStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuditRetentionMonths = table.Column<int>(type: "integer", nullable: false),
                    WalletAddress = table.Column<string>(type: "text", nullable: true),
                    PublicKey = table.Column<string>(type: "text", nullable: true),
                    EncryptionPublicKey = table.Column<string>(type: "text", nullable: true),
                    SigningAlgorithm = table.Column<string>(type: "text", nullable: true),
                    Branding = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgDidDocuments",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryDid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FederatedDid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DocumentJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    KeyVersionFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastRegeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRegenerationReason = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgDidDocuments", x => x.Id);
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
                name: "OrgRecoveryConfigs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecoveryPublicKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecoveryKeyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgRecoveryConfigs", x => x.Id);
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
                name: "PlatformSettings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicOrgEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaxOrgsPerUser = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUsers",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VerificationTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PasswordResetTokenHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PasswordResetTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedPermanently = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOrgsCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WelcomeSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUsers", x => x.Id);
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
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Roles = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProvisionedVia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileCompleted = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "OrganizationRegisterSubscriptions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegisterName = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: true),
                    SubscriptionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InvitationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubscribedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubscribedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRegisterSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationRegisterSubscriptions_Organizations_Organizatio~",
                        column: x => x.OrganizationId,
                        principalSchema: "public",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegisterInvitationRecords",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceOrgId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetOrgDid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetWalletAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegisterName = table.Column<string>(type: "character varying(38)", maxLength: 38, nullable: true),
                    Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisterInvitationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegisterInvitationRecords_Organizations_SourceOrgId",
                        column: x => x.SourceOrgId,
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
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerificationMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "challenge-verify")
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
                name: "AuthChallengeTokens",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScopedOperation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthChallengeTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthChallengeTokens_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboxEntries",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DetailHref = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IconKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChannelHints = table.Column<int>(type: "integer", nullable: false),
                    WriterServiceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxEntries_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.ForeignKey(
                        name: "FK_PasskeyCredentials_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSocialLogins",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSocialLogins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformSocialLogins_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUserDevices",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DevicePublicJwkThumbprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DevicePublicJwkJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByPlatformUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DelegationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DelegationCredentialJti = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusListId = table.Column<int>(type: "integer", nullable: false),
                    StatusListIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUserDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformUserDevices_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUserOrgMemberships",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUserOrgMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlatformUserOrgMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "public",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlatformUserOrgMemberships_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlatformUserPersonas",
                schema: "public",
                columns: table => new
                {
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    // Feature 125 — per-context persona scoping. Guid.Empty
                    // (all-zero) represents the Personal context.
                    ContextOrgId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: Guid.Empty),
                    CiphertextBlob = table.Column<byte[]>(type: "bytea", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", maxLength: 24, nullable: false),
                    WrappedKeyRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformUserPersonas", x => new { x.PlatformUserId, x.ContextOrgId });
                    table.ForeignKey(
                        name: "FK_PlatformUserPersonas_PlatformUsers_PlatformUserId",
                        column: x => x.PlatformUserId,
                        principalSchema: "public",
                        principalTable: "PlatformUsers",
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
                name: "IX_AuditLog_Org_Time",
                schema: "public",
                table: "AuditLogEntries",
                columns: new[] { "OrganizationId", "Timestamp" },
                descending: new[] { false, true });

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
                name: "IX_AuthChallengeToken_ExpiresAt",
                schema: "public",
                table: "AuthChallengeTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthChallengeToken_User_Active",
                schema: "public",
                table: "AuthChallengeTokens",
                columns: new[] { "PlatformUserId", "ConsumedAt" },
                filter: "\"ConsumedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_AuthChallengeToken_TokenHash",
                schema: "public",
                table: "AuthChallengeTokens",
                column: "TokenHash",
                unique: true);

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
                name: "IX_IdentityProviderConfigurations_ProviderPreset",
                schema: "public",
                table: "IdentityProviderConfigurations",
                column: "ProviderPreset");

            migrationBuilder.CreateIndex(
                name: "UQ_IdpConfig_Org_Provider",
                schema: "public",
                table: "IdentityProviderConfigurations",
                columns: new[] { "OrganizationId", "ProviderPreset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_Category_OccurredAt",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "Category", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_CorrelationKey_OccurredAt",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "CorrelationKey", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_OccurredAt",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxEntries_PlatformUserId_SourceEventId",
                schema: "public",
                table: "InboxEntries",
                columns: new[] { "PlatformUserId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvNonce_Nonce",
                schema: "public",
                table: "InvitationNonces",
                column: "Nonce",
                unique: true);

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
                name: "IX_OrgRegSub_Org_Active",
                schema: "public",
                table: "OrganizationRegisterSubscriptions",
                column: "OrganizationId",
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_OrgRegSub_OrgId_RegisterId",
                schema: "public",
                table: "OrganizationRegisterSubscriptions",
                columns: new[] { "OrganizationId", "RegisterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgRegSub_RegisterId",
                schema: "public",
                table: "OrganizationRegisterSubscriptions",
                column: "RegisterId");

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
                name: "IX_OrgDidDocuments_FederatedDid",
                schema: "public",
                table: "OrgDidDocuments",
                column: "FederatedDid");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDidDocuments_OrganizationId",
                schema: "public",
                table: "OrgDidDocuments",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgDidDocuments_PrimaryDid",
                schema: "public",
                table: "OrgDidDocuments",
                column: "PrimaryDid");

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
                name: "IX_OrgRecoveryConfig_OrganizationId",
                schema: "public",
                table: "OrgRecoveryConfigs",
                column: "OrganizationId",
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
                name: "IX_PasskeyCredential_PlatformUserId",
                schema: "public",
                table: "PasskeyCredentials",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredential_PlatformUserId_Status",
                schema: "public",
                table: "PasskeyCredentials",
                columns: new[] { "PlatformUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredentials_CredentialId",
                schema: "public",
                table: "PasskeyCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSocialLogin_PlatformUserId",
                schema: "public",
                table: "PlatformSocialLogins",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformSocialLogin_Provider_Subject",
                schema: "public",
                table: "PlatformSocialLogins",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserDevices_DevicePublicJwkThumbprint",
                schema: "public",
                table: "PlatformUserDevices",
                column: "DevicePublicJwkThumbprint");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserDevices_PlatformUserId_Status",
                schema: "public",
                table: "PlatformUserDevices",
                columns: new[] { "PlatformUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserDevices_StatusListIndex",
                schema: "public",
                table: "PlatformUserDevices",
                column: "StatusListIndex");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUserOrgMemberships_OrganizationId",
                schema: "public",
                table: "PlatformUserOrgMemberships",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformUserOrgMembership_User_Org",
                schema: "public",
                table: "PlatformUserOrgMemberships",
                columns: new[] { "PlatformUserId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUser_PasswordResetTokenHash",
                schema: "public",
                table: "PlatformUsers",
                column: "PasswordResetTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformUser_Status",
                schema: "public",
                table: "PlatformUsers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformUser_Email",
                schema: "public",
                table: "PlatformUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformUser_VerificationToken",
                schema: "public",
                table: "PlatformUsers",
                column: "VerificationToken",
                unique: true,
                filter: "\"VerificationToken\" IS NOT NULL");

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
                name: "IX_RegInvRec_InvitationId",
                schema: "public",
                table: "RegisterInvitationRecords",
                column: "InvitationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegInvRec_SourceOrgId",
                schema: "public",
                table: "RegisterInvitationRecords",
                column: "SourceOrgId");

            migrationBuilder.CreateIndex(
                name: "IX_RegInvRec_SourceOrgId_Status",
                schema: "public",
                table: "RegisterInvitationRecords",
                columns: new[] { "SourceOrgId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RegInvRec_TargetOrgDid",
                schema: "public",
                table: "RegisterInvitationRecords",
                column: "TargetOrgDid");

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
                name: "UQ_TotpConfiguration_UserId",
                schema: "public",
                table: "TotpConfigurations",
                column: "UserId",
                unique: true);

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
                name: "IX_UserIdentity_PlatformUserId",
                schema: "public",
                table: "UserIdentities",
                column: "PlatformUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_UserIdentity_Org_Email",
                schema: "public",
                table: "UserIdentities",
                columns: new[] { "OrganizationId", "Email" },
                unique: true);

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
                name: "AuthChallengeTokens",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CustomDomainMappings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "IdentityProviderConfigurations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "InboxEntries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "InvitationNonces",
                schema: "public");

            migrationBuilder.DropTable(
                name: "LinkedWalletAddresses",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrganizationPermissionConfigurations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrganizationRegisterSubscriptions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrgDidDocuments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrgInvitations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "OrgRecoveryConfigs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ParticipantAuditEntries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PasskeyCredentials",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PlatformSettings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PlatformSocialLogins",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PlatformUserDevices",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PlatformUserOrgMemberships",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PlatformUserPersonas",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PushSubscriptions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "RegisterInvitationRecords",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServicePrincipals",
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
                name: "PlatformUsers",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ParticipantIdentities",
                schema: "public");
        }
    }
}
