// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data;

/// <summary>
/// Entity Framework Core database context for the Tenant Service.
/// Supports multi-tenant schema isolation (public schema + per-org schemas).
/// </summary>
public class TenantDbContext : DbContext
{
    private readonly string _currentSchema;

    /// <summary>
    /// Creates a new TenantDbContext instance.
    /// </summary>
    /// <param name="options">DbContext options (configured for PostgreSQL or InMemory).</param>
    /// <param name="schema">Current schema to use (default: "public"). For tenant data, use "org_{organizationId}".</param>
    public TenantDbContext(DbContextOptions<TenantDbContext> options, string schema = "public")
        : base(options)
    {
        _currentSchema = schema;
    }

    // Public schema entities (shared metadata)
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<IdentityProviderConfiguration> IdentityProviderConfigurations => Set<IdentityProviderConfiguration>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformSocialLogin> PlatformSocialLogins => Set<PlatformSocialLogin>();
    public DbSet<PlatformUserOrgMembership> PlatformUserOrgMemberships => Set<PlatformUserOrgMembership>();
    public DbSet<PlatformUserPersona> PlatformUserPersonas => Set<PlatformUserPersona>();
    public DbSet<PlatformUserDevice> PlatformUserDevices => Set<PlatformUserDevice>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();
    public DbSet<AuthChallengeToken> AuthChallengeTokens => Set<AuthChallengeToken>();
    public DbSet<ServicePrincipal> ServicePrincipals => Set<ServicePrincipal>();
    public DbSet<OrgRecoveryConfig> OrgRecoveryConfigs => Set<OrgRecoveryConfig>();
    public DbSet<OrganizationRegisterSubscription> OrganizationRegisterSubscriptions => Set<OrganizationRegisterSubscription>();
    public DbSet<RegisterInvitationRecord> RegisterInvitationRecords => Set<RegisterInvitationRecord>();
    public DbSet<InvitationNonce> InvitationNonces => Set<InvitationNonce>();
    public DbSet<InboxEntry> InboxEntries => Set<InboxEntry>();

    /// <summary>Feature 120 US2 — published per-org DID documents.</summary>
    public DbSet<OrgDidDocument> OrgDidDocuments => Set<OrgDidDocument>();

    // Public schema entities for custom domain resolution
    public DbSet<CustomDomainMapping> CustomDomainMappings => Set<CustomDomainMapping>();

    // Per-tenant schema entities (isolated per organization)
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<OrgInvitation> OrgInvitations => Set<OrgInvitation>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<TotpConfiguration> TotpConfigurations => Set<TotpConfiguration>();
    public DbSet<OrganizationPermissionConfiguration> OrganizationPermissionConfigurations => Set<OrganizationPermissionConfiguration>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<ParticipantIdentity> ParticipantIdentities => Set<ParticipantIdentity>();
    public DbSet<ParticipantAuditEntry> ParticipantAuditEntries => Set<ParticipantAuditEntry>();

    // Public schema entities for participant wallet linking (platform-wide uniqueness)
    public DbSet<LinkedWalletAddress> LinkedWalletAddresses => Set<LinkedWalletAddress>();
    public DbSet<WalletLinkChallenge> WalletLinkChallenges => Set<WalletLinkChallenge>();

    // Public schema entities for push notifications
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    // Public schema entities for activity event log
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    // Public schema entities for platform-level configuration
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Set default schema (public or org_{id})
        // Note: InMemory and SQLite providers don't support schemas, so only set for PostgreSQL
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
        if (!isInMemory)
        {
            modelBuilder.HasDefaultSchema(_currentSchema);
        }

        // Configure Organization entity
        ConfigureOrganization(modelBuilder);

        // Configure IdentityProviderConfiguration entity
        ConfigureIdentityProviderConfiguration(modelBuilder);

        // Configure PasskeyCredential entity (public schema)
        ConfigurePasskeyCredential(modelBuilder);

        // Configure ServicePrincipal entity
        ConfigureServicePrincipal(modelBuilder);

        // Configure OrgRecoveryConfig entity (Feature 060)
        ConfigureOrgRecoveryConfig(modelBuilder);

        // Configure OrganizationRegisterSubscription entity (public schema)
        ConfigureOrganizationRegisterSubscription(modelBuilder);

        // Configure RegisterInvitationRecord and InvitationNonce entities (public schema)
        ConfigureRegisterInvitations(modelBuilder);

        // Configure UserIdentity entity (per-org schema)
        ConfigureUserIdentity(modelBuilder);

        // Configure UserPreferences entity (per-org schema)
        ConfigureUserPreferences(modelBuilder);

        // Configure TotpConfiguration entity (per-org schema)
        ConfigureTotpConfiguration(modelBuilder);

        // Configure OrganizationPermissionConfiguration entity (per-org schema)
        ConfigureOrganizationPermissionConfiguration(modelBuilder);

        // Configure AuditLogEntry entity (per-org schema)
        ConfigureAuditLogEntry(modelBuilder);

        // Configure ParticipantIdentity entity (per-org schema)
        ConfigureParticipantIdentity(modelBuilder);

        // Configure ParticipantAuditEntry entity (per-org schema)
        ConfigureParticipantAuditEntry(modelBuilder);

        // Configure LinkedWalletAddress entity (public schema)
        ConfigureLinkedWalletAddress(modelBuilder);

        // Configure WalletLinkChallenge entity (public schema)
        ConfigureWalletLinkChallenge(modelBuilder);

        // Configure PushSubscription entity (public schema)
        ConfigurePushSubscription(modelBuilder);

        // Configure SystemConfiguration entity (public schema)
        ConfigureSystemConfiguration(modelBuilder);

        // Configure OrgInvitation entity (per-org schema)
        ConfigureOrgInvitation(modelBuilder);

        // Configure CustomDomainMapping entity (public schema)
        ConfigureCustomDomainMapping(modelBuilder);

        // Configure ActivityEvent entity (public schema)
        ConfigureActivityEvent(modelBuilder);

        // Configure PlatformUser entity (public schema)
        ConfigurePlatformUser(modelBuilder);

        // Configure PlatformSocialLogin entity (public schema)
        ConfigurePlatformSocialLogin(modelBuilder);

        // Configure PlatformUserOrgMembership entity (public schema)
        ConfigurePlatformUserOrgMembership(modelBuilder);

        // Configure PlatformSettings entity (public schema)
        ConfigurePlatformSettings(modelBuilder);

        // Configure PlatformUserPersona entity (public schema) — Feature 092
        ConfigurePlatformUserPersona(modelBuilder);

        // Configure PlatformUserDevice entity (public schema) — Feature 114
        ConfigurePlatformUserDevice(modelBuilder);

        // Configure AuthChallengeToken entity (public schema) — Feature 116
        ConfigureAuthChallengeToken(modelBuilder);

        // Configure InboxEntry entity (public schema) — Feature 118 / US3 (durable user inbox)
        ConfigureInboxEntry(modelBuilder);

        // Configure OrgDidDocument entity (public schema) — Feature 120 US2.
        ConfigureOrgDidDocument(modelBuilder);
    }

    /// <summary>Feature 120 US2 — published per-org DID documents.</summary>
    private void ConfigureOrgDidDocument(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<OrgDidDocument>(entity =>
        {
            if (isInMemory)
                entity.ToTable("OrgDidDocuments");
            else
                entity.ToTable("OrgDidDocuments", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.OrganizationId).IsRequired();
            entity.Property(e => e.PrimaryDid).IsRequired().HasMaxLength(200);
            entity.Property(e => e.FederatedDid).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DocumentJson).IsRequired().HasMaxLength(16384);
            entity.Property(e => e.KeyVersionFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(e => e.LastRegenerationReason).HasConversion<int>().IsRequired();
            entity.Property(e => e.LastRegeneratedAt).IsRequired();
            entity.Property(e => e.Version).IsRequired();

            entity.HasIndex(e => e.OrganizationId).IsUnique()
                .HasDatabaseName("IX_OrgDidDocuments_OrganizationId");
            entity.HasIndex(e => e.PrimaryDid)
                .HasDatabaseName("IX_OrgDidDocuments_PrimaryDid");
            entity.HasIndex(e => e.FederatedDid)
                .HasDatabaseName("IX_OrgDidDocuments_FederatedDid");
        });
    }

    /// <summary>Feature 118 / US3 — durable per-user notification entries.</summary>
    private void ConfigureInboxEntry(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<InboxEntry>(entity =>
        {
            if (isInMemory)
                entity.ToTable("InboxEntries");
            else
                entity.ToTable("InboxEntries", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Category)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(e => e.Severity)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(e => e.CorrelationKey)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.DetailHref)
                .IsRequired()
                .HasMaxLength(1024);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Summary)
                .HasMaxLength(1000);

            entity.Property(e => e.IconKey)
                .HasMaxLength(64);

            entity.Property(e => e.OccurredAt).IsRequired();

            // Idempotency on duplicate writer retries.
            entity.HasIndex(e => new { e.PlatformUserId, e.SourceEventId })
                .IsUnique()
                .HasDatabaseName("IX_InboxEntries_PlatformUserId_SourceEventId");

            // Primary list query.
            entity.HasIndex(e => new { e.PlatformUserId, e.OccurredAt })
                .HasDatabaseName("IX_InboxEntries_PlatformUserId_OccurredAt");

            // Sibling-grouping lookup (30s correlation-key window).
            entity.HasIndex(e => new { e.PlatformUserId, e.CorrelationKey, e.OccurredAt })
                .HasDatabaseName("IX_InboxEntries_PlatformUserId_CorrelationKey_OccurredAt");

            // Category filter.
            entity.HasIndex(e => new { e.PlatformUserId, e.Category, e.OccurredAt })
                .HasDatabaseName("IX_InboxEntries_PlatformUserId_Category_OccurredAt");

            // Cascade delete with PlatformUser.
            entity.HasOne<PlatformUser>()
                .WithMany()
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureOrganization(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<Organization>(entity =>
        {
            if (isInMemory)
                entity.ToTable("Organizations");
            else
                entity.ToTable("Organizations", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Subdomain)
                .IsRequired()
                .HasMaxLength(50);

            entity.HasIndex(e => e.Subdomain)
                .IsUnique();

            entity.HasIndex(e => e.Status);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired();

            // JSON column for branding configuration
            // Note: To resolve EF Core warning about optional dependents with table sharing,
            // we use the ToJson() method to store branding as a JSON column instead of table sharing.
            // This makes it clear that a null JSON value means no branding configuration.
            entity.OwnsOne(e => e.Branding, branding =>
            {
                branding.ToJson();
                branding.Property(b => b.LogoUrl).HasMaxLength(500);
                branding.Property(b => b.PrimaryColor).HasMaxLength(20);
                branding.Property(b => b.SecondaryColor).HasMaxLength(20);
                branding.Property(b => b.CompanyTagline).HasMaxLength(500);
            });

            entity.Property(e => e.OrgType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CustomDomain).HasMaxLength(500);
            entity.Property(e => e.CustomDomainStatus).HasConversion<string>().HasMaxLength(20);

            // One-to-many relationship with IdentityProviderConfiguration
            entity.HasMany(e => e.IdentityProviders)
                .WithOne(i => i.Organization)
                .HasForeignKey(i => i.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureIdentityProviderConfiguration(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<IdentityProviderConfiguration>(entity =>
        {
            if (isInMemory)
                entity.ToTable("IdentityProviderConfigurations");
            else
                entity.ToTable("IdentityProviderConfigurations", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.IssuerUrl)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.ClientId)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.ClientSecretEncrypted)
                .IsRequired();

            entity.Property(e => e.Scopes)
                .IsRequired();

            entity.Property(e => e.AuthorizationEndpoint)
                .HasMaxLength(500);

            entity.Property(e => e.TokenEndpoint)
                .HasMaxLength(500);

            entity.Property(e => e.MetadataUrl)
                .HasMaxLength(500);

            entity.Property(e => e.ProviderPreset)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.UserInfoEndpoint).HasMaxLength(500);
            entity.Property(e => e.JwksUri).HasMaxLength(500);
            entity.Property(e => e.DisplayName).HasMaxLength(200);

            entity.HasIndex(e => new { e.OrganizationId, e.ProviderPreset })
                .IsUnique()
                .HasDatabaseName("UQ_IdpConfig_Org_Provider");

            entity.HasIndex(e => e.ProviderPreset);
        });
    }

    private void ConfigurePasskeyCredential(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PasskeyCredential>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PasskeyCredentials");
            else
                entity.ToTable("PasskeyCredentials", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CredentialId)
                .IsRequired();

            entity.Property(e => e.PublicKeyCose)
                .IsRequired();

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.DeviceType)
                .IsRequired(false)
                .HasMaxLength(100);

            entity.Property(e => e.AttestationType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.DisabledReason)
                .IsRequired(false)
                .HasMaxLength(500);

            entity.HasIndex(e => e.CredentialId)
                .IsUnique();

            entity.HasOne(e => e.PlatformUser)
                .WithMany(p => p.PasskeyCredentials)
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PlatformUserId)
                .HasDatabaseName("IX_PasskeyCredential_PlatformUserId");

            entity.HasIndex(e => new { e.PlatformUserId, e.Status })
                .HasDatabaseName("IX_PasskeyCredential_PlatformUserId_Status");
        });
    }

    private void ConfigureServicePrincipal(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<ServicePrincipal>(entity =>
        {
            if (isInMemory)
                entity.ToTable("ServicePrincipals");
            else
                entity.ToTable("ServicePrincipals", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ServiceName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.ClientId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.ClientSecretEncrypted)
                .IsRequired();

            entity.Property(e => e.Scopes)
                .IsRequired();

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired();

            entity.HasIndex(e => e.ServiceName)
                .IsUnique();

            entity.HasIndex(e => e.ClientId)
                .IsUnique();
        });
    }

    private void ConfigureUserIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserIdentity>(entity =>
        {
            entity.ToTable("UserIdentities");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.Roles)
                .HasConversion(
                    v => string.Join(',', v.Select(r => r.ToString())),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => Enum.Parse<UserRole>(s))
                          .ToArray())
                .IsRequired();

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.PlatformUserId).IsRequired();
            entity.HasIndex(e => e.PlatformUserId)
                .HasDatabaseName("IX_UserIdentity_PlatformUserId");

            entity.Property(e => e.ProvisionedVia).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(e => new { e.OrganizationId, e.Email })
                .IsUnique()
                .HasDatabaseName("UQ_UserIdentity_Org_Email");  // Email must be unique within organization

            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.Status);

        });
    }

    private void ConfigureOrganizationPermissionConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationPermissionConfiguration>(entity =>
        {
            entity.ToTable("OrganizationPermissionConfigurations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ApprovedBlockchains)
                .IsRequired();

            entity.HasIndex(e => e.OrganizationId)
                .IsUnique();
        });
    }

    private void ConfigureAuditLogEntry(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("AuditLogEntries");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.IpAddress)
                .HasMaxLength(45); // IPv6 max length

            // Details stored as JSON (EF Core will handle serialization)
            if (isInMemory)
            {
                // InMemory provider needs a value converter for Dictionary<string, object>
                entity.Property(e => e.Details)
                    .HasConversion(
                        v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => v == null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(v, (System.Text.Json.JsonSerializerOptions?)null));
            }
            else
            {
                entity.Property(e => e.Details)
                    .HasColumnType("jsonb"); // PostgreSQL JSONB
            }

            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.IdentityId);
            entity.HasIndex(e => e.OrganizationId);

            // Composite covering the AuditCleanupService per-org retention sweep
            // (WHERE OrganizationId = X AND Timestamp < cutoff) and the audit-log
            // viewer's "this org, newest first" listing.
            entity.HasIndex(e => new { e.OrganizationId, e.Timestamp })
                .IsDescending(false, true)
                .HasDatabaseName("IX_AuditLog_Org_Time");
        });
    }

    private void ConfigureParticipantIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParticipantIdentity>(entity =>
        {
            entity.ToTable("ParticipantIdentities");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            // Unique constraint: one participant identity per user per organization
            entity.HasIndex(e => new { e.UserId, e.OrganizationId })
                .IsUnique()
                .HasDatabaseName("UQ_Participant_User_Org");

            // Index for org-based queries with status filter
            entity.HasIndex(e => new { e.OrganizationId, e.Status })
                .HasDatabaseName("IX_Participant_Org_Status");

            entity.HasIndex(e => e.UserId);

            // Relationships (navigation properties configured, cascade delete disabled for audit trail)
            entity.HasMany(e => e.LinkedWalletAddresses)
                .WithOne(e => e.Participant)
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.AuditEntries)
                .WithOne(e => e.Participant)
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.WalletLinkChallenges)
                .WithOne(e => e.Participant)
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureParticipantAuditEntry(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<ParticipantAuditEntry>(entity =>
        {
            entity.ToTable("ParticipantAuditEntries");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ActorId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.ActorType)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.IpAddress)
                .HasMaxLength(45); // IPv6 max length

            // JSON columns for old/new values
            if (isInMemory)
            {
                // InMemory provider needs value converters for JsonDocument
                entity.Property(e => e.OldValues)
                    .HasConversion(
                        v => v == null ? null : v.RootElement.GetRawText(),
                        v => v == null ? null : System.Text.Json.JsonDocument.Parse(v, default(System.Text.Json.JsonDocumentOptions)));

                entity.Property(e => e.NewValues)
                    .HasConversion(
                        v => v == null ? null : v.RootElement.GetRawText(),
                        v => v == null ? null : System.Text.Json.JsonDocument.Parse(v, default(System.Text.Json.JsonDocumentOptions)));
            }
            else
            {
                entity.Property(e => e.OldValues)
                    .HasColumnType("jsonb");

                entity.Property(e => e.NewValues)
                    .HasColumnType("jsonb");
            }

            // Index for participant-based queries sorted by time
            entity.HasIndex(e => new { e.ParticipantId, e.Timestamp })
                .IsDescending(false, true)
                .HasDatabaseName("IX_Audit_Participant_Time");

            // Index for actor-based queries sorted by time
            entity.HasIndex(e => new { e.ActorId, e.Timestamp })
                .IsDescending(false, true)
                .HasDatabaseName("IX_Audit_Actor_Time");
        });
    }

    private void ConfigureLinkedWalletAddress(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<LinkedWalletAddress>(entity =>
        {
            if (isInMemory)
                entity.ToTable("LinkedWalletAddresses");
            else
                entity.ToTable("LinkedWalletAddresses", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.WalletAddress)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.PublicKey)
                .IsRequired();

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.VerificationMethod)
                .IsRequired()
                .HasMaxLength(30)
                .HasDefaultValue(WalletVerificationMethod.ChallengeVerify);

            // Partial unique index: only one active link per wallet address platform-wide
            if (isInMemory)
            {
                // InMemory doesn't support filtered indexes, use regular unique index
                entity.HasIndex(e => e.WalletAddress)
                    .HasDatabaseName("IX_WalletLink_Address");
            }
            else
            {
                entity.HasIndex(e => e.WalletAddress)
                    .IsUnique()
                    .HasFilter("\"Status\" = 'Active'")
                    .HasDatabaseName("UQ_Active_WalletAddress");

                // Additional non-unique index for lookups
                entity.HasIndex(e => e.WalletAddress)
                    .HasDatabaseName("IX_WalletLink_Address");
            }

            entity.HasIndex(e => e.ParticipantId)
                .HasDatabaseName("IX_WalletLink_Participant");
        });
    }

    private void ConfigureWalletLinkChallenge(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<WalletLinkChallenge>(entity =>
        {
            if (isInMemory)
                entity.ToTable("WalletLinkChallenges");
            else
                entity.ToTable("WalletLinkChallenges", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.WalletAddress)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Challenge)
                .IsRequired()
                .HasMaxLength(1024);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            // Index for participant + status queries
            entity.HasIndex(e => new { e.ParticipantId, e.Status })
                .HasDatabaseName("IX_Challenge_Participant_Status");

            // Index for address + status queries
            entity.HasIndex(e => new { e.WalletAddress, e.Status })
                .HasDatabaseName("IX_Challenge_Address_Status");
        });
    }

    private void ConfigureUserPreferences(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserPreferences>(entity =>
        {
            entity.ToTable("UserPreferences");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Theme)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Language)
                .IsRequired()
                .HasMaxLength(5);

            entity.Property(e => e.TimeFormat)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.DefaultWalletAddress)
                .HasMaxLength(200);

            entity.Property(e => e.NotificationMethod)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(NotificationMethod.InApp);

            entity.Property(e => e.NotificationFrequency)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(NotificationFrequency.RealTime);

            // Unique index: one preferences record per user
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("UQ_UserPreferences_UserId");
        });
    }

    private void ConfigureTotpConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TotpConfiguration>(entity =>
        {
            entity.ToTable("TotpConfigurations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EncryptedSecret)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.BackupCodes)
                .IsRequired()
                .HasMaxLength(2000);

            // Unique index: one TOTP config per user
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("UQ_TotpConfiguration_UserId");
        });
    }

    private void ConfigurePushSubscription(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PushSubscriptions");
            else
                entity.ToTable("PushSubscriptions", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Endpoint)
                .IsRequired()
                .HasMaxLength(2000);

            entity.Property(e => e.P256dhKey)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.AuthKey)
                .IsRequired()
                .HasMaxLength(500);

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_PushSubscription_UserId");

            entity.HasIndex(e => new { e.UserId, e.Endpoint })
                .IsUnique()
                .HasDatabaseName("UQ_PushSubscription_User_Endpoint");
        });
    }

    private void ConfigureSystemConfiguration(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<SystemConfiguration>(entity =>
        {
            if (isInMemory)
                entity.ToTable("SystemConfigurations");
            else
                entity.ToTable("SystemConfigurations", "public");

            entity.HasKey(e => e.Key);

            entity.Property(e => e.Key)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Value)
                .IsRequired()
                .HasMaxLength(500);
        });
    }

    private void ConfigureOrgInvitation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrgInvitation>(entity =>
        {
            entity.ToTable("OrgInvitations");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AssignedRole).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => new { e.OrganizationId, e.Email, e.Status });
        });
    }

    private void ConfigureCustomDomainMapping(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<CustomDomainMapping>(entity =>
        {
            if (isInMemory)
                entity.ToTable("CustomDomainMappings");
            else
                entity.ToTable("CustomDomainMappings", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Domain).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(e => e.Domain).IsUnique();
            entity.HasIndex(e => e.OrganizationId).IsUnique();
        });
    }

    private void ConfigureActivityEvent(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<ActivityEvent>(entity =>
        {
            if (isInMemory)
                entity.ToTable("ActivityEvents");
            else
                entity.ToTable("ActivityEvents", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Severity).IsRequired()
                .HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.SourceService).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntityId).HasMaxLength(200);
            entity.Property(e => e.EntityType).HasMaxLength(50);

            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                .HasDatabaseName("IX_ActivityEvent_UserId_CreatedAt")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.OrganizationId, e.CreatedAt })
                .HasDatabaseName("IX_ActivityEvent_OrgId_CreatedAt")
                .IsDescending(false, true);
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_ActivityEvent_ExpiresAt");

            if (!isInMemory)
            {
                entity.HasIndex(e => new { e.UserId, e.IsRead })
                    .HasDatabaseName("IX_ActivityEvent_UserId_IsRead")
                    .HasFilter("\"IsRead\" = false");
            }
            else
            {
                entity.HasIndex(e => new { e.UserId, e.IsRead })
                    .HasDatabaseName("IX_ActivityEvent_UserId_IsRead");
            }
        });
    }

    private void ConfigurePlatformUser(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PlatformUser>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PlatformUsers");
            else
                entity.ToTable("PlatformUsers", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(320);

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.PasswordHash)
                .IsRequired(false)
                .HasMaxLength(500);

            entity.Property(e => e.VerificationToken)
                .IsRequired(false)
                .HasMaxLength(100);

            entity.Property(e => e.PasswordResetTokenHash)
                .IsRequired(false)
                .HasMaxLength(500);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(e => e.Email)
                .IsUnique()
                .HasDatabaseName("UQ_PlatformUser_Email");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_PlatformUser_Status");

            entity.HasIndex(e => e.PasswordResetTokenHash)
                .HasDatabaseName("IX_PlatformUser_PasswordResetTokenHash");

            // EmailVerificationService.VerifyTokenAsync looks up users by raw token;
            // partial-unique on the populated rows keeps the index small and avoids
            // a sequential scan on every verify-email click.
            entity.HasIndex(e => e.VerificationToken)
                .IsUnique()
                .HasFilter("\"VerificationToken\" IS NOT NULL")
                .HasDatabaseName("UQ_PlatformUser_VerificationToken");

            entity.HasMany(e => e.SocialLogins)
                .WithOne(s => s.PlatformUser)
                .HasForeignKey(s => s.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.OrgMemberships)
                .WithOne(m => m.PlatformUser)
                .HasForeignKey(m => m.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Feature 092: Consumer persona — one-to-one with PlatformUser, cascade delete.
    private void ConfigurePlatformUserPersona(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PlatformUserPersona>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PlatformUserPersonas");
            else
                entity.ToTable("PlatformUserPersonas", "public");

            entity.HasKey(e => e.PlatformUserId);

            entity.Property(e => e.CiphertextBlob)
                .IsRequired();

            entity.Property(e => e.Nonce)
                .IsRequired()
                .HasMaxLength(24);

            entity.Property(e => e.WrappedKeyRef)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.SchemaVersion)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .IsRequired();

            // One-to-one with PlatformUser. Cascade delete is load-bearing
            // for FR-007a — the persona must be wiped atomically with the
            // user account (GDPR right-to-erasure).
            entity.HasOne(e => e.PlatformUser)
                .WithOne()
                .HasForeignKey<PlatformUserPersona>(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Feature 114: Citizen wallet device enrolment — many-to-one with PlatformUser,
    // cascade delete. Indexed for: (PlatformUserId, Status) device list queries,
    // DevicePublicJwkThumbprint enrolment-dedupe lookup, StatusListIndex publish.
    private void ConfigurePlatformUserDevice(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PlatformUserDevice>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PlatformUserDevices");
            else
                entity.ToTable("PlatformUserDevices", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Label)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(e => e.DevicePublicJwkThumbprint)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.DevicePublicJwkJson)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Platform)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(512);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            entity.Property(e => e.EnrolledAt).IsRequired();
            entity.Property(e => e.DelegationExpiresAt).IsRequired();

            entity.Property(e => e.DelegationCredentialJti)
                .IsRequired()
                .HasMaxLength(64);

            entity.HasIndex(e => new { e.PlatformUserId, e.Status })
                .HasDatabaseName("IX_PlatformUserDevices_PlatformUserId_Status");

            entity.HasIndex(e => e.DevicePublicJwkThumbprint)
                .HasDatabaseName("IX_PlatformUserDevices_DevicePublicJwkThumbprint");

            entity.HasIndex(e => e.StatusListIndex)
                .HasDatabaseName("IX_PlatformUserDevices_StatusListIndex");

            // Many-to-one with PlatformUser. Cascade delete keeps device records
            // in lock-step with the owning identity.
            entity.HasOne(e => e.PlatformUser)
                .WithMany()
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Feature 116: Re-authentication challenge tokens — single-use, scoped, 5-min TTL.
    // Hash-only storage (raw token never persisted). Atomic consume via
    // UPDATE … WHERE ConsumedAt IS NULL in IAuthChallengeRepository.TryConsumeAsync.
    private void ConfigureAuthChallengeToken(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<AuthChallengeToken>(entity =>
        {
            if (isInMemory)
                entity.ToTable("AuthChallengeTokens");
            else
                entity.ToTable("AuthChallengeTokens", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.TokenHash)
                .IsRequired()
                .HasMaxLength(64); // SHA-256 hex output is exactly 64 chars

            entity.Property(e => e.Method)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(e => e.ScopedOperation)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(24);

            entity.Property(e => e.IssuedAt).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();

            // Lookup by SHA-256(headerValue) on every gated mutation.
            entity.HasIndex(e => e.TokenHash)
                .IsUnique()
                .HasDatabaseName("UQ_AuthChallengeToken_TokenHash");

            // Filtered index for the "list active challenges for user X" debug path.
            // InMemory lacks filtered-index support; fall back to a regular composite.
            if (isInMemory)
            {
                entity.HasIndex(e => new { e.PlatformUserId, e.ConsumedAt })
                    .HasDatabaseName("IX_AuthChallengeToken_User_Active");
            }
            else
            {
                entity.HasIndex(e => new { e.PlatformUserId, e.ConsumedAt })
                    .HasFilter("\"ConsumedAt\" IS NULL")
                    .HasDatabaseName("IX_AuthChallengeToken_User_Active");
            }

            // Drives the daily prune query in AuthChallengeTokenCleanupService.
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("IX_AuthChallengeToken_ExpiresAt");

            entity.HasOne(e => e.PlatformUser)
                .WithMany()
                .HasForeignKey(e => e.PlatformUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigurePlatformSocialLogin(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PlatformSocialLogin>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PlatformSocialLogins");
            else
                entity.ToTable("PlatformSocialLogins", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Provider)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Subject)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Email)
                .IsRequired(false)
                .HasMaxLength(320);

            entity.Property(e => e.DisplayName)
                .IsRequired(false)
                .HasMaxLength(256);

            entity.HasIndex(e => new { e.Provider, e.Subject })
                .IsUnique()
                .HasDatabaseName("UQ_PlatformSocialLogin_Provider_Subject");

            entity.HasIndex(e => e.PlatformUserId)
                .HasDatabaseName("IX_PlatformSocialLogin_PlatformUserId");
        });
    }

    private void ConfigurePlatformUserOrgMembership(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PlatformUserOrgMembership>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PlatformUserOrgMemberships");
            else
                entity.ToTable("PlatformUserOrgMemberships", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Role)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(e => new { e.PlatformUserId, e.OrganizationId })
                .IsUnique()
                .HasDatabaseName("UQ_PlatformUserOrgMembership_User_Org");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigurePlatformSettings(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PlatformSettings>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PlatformSettings");
            else
                entity.ToTable("PlatformSettings", "public");
            entity.HasKey(e => e.Id);
        });
    }

    private void ConfigureOrgRecoveryConfig(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.IsInMemory();

        modelBuilder.Entity<OrgRecoveryConfig>(entity =>
        {
            if (isInMemory)
                entity.ToTable("OrgRecoveryConfigs");
            else
                entity.ToTable("OrgRecoveryConfigs", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RecoveryPublicKey)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.RecoveryKeyId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.CreatedBy)
                .IsRequired()
                .HasMaxLength(256);

            entity.HasIndex(e => e.OrganizationId)
                .IsUnique()
                .HasDatabaseName("IX_OrgRecoveryConfig_OrganizationId");
        });
    }

    private void ConfigureOrganizationRegisterSubscription(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.IsInMemory();

        modelBuilder.Entity<OrganizationRegisterSubscription>(entity =>
        {
            if (isInMemory)
                entity.ToTable("OrganizationRegisterSubscriptions");
            else
                entity.ToTable("OrganizationRegisterSubscriptions", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.RegisterId)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.RegisterName)
                .HasMaxLength(38);

            entity.Property(e => e.SubscriptionType)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(e => e.InvitationId)
                .HasMaxLength(64);

            entity.HasIndex(e => new { e.OrganizationId, e.RegisterId })
                .IsUnique()
                .HasDatabaseName("IX_OrgRegSub_OrgId_RegisterId");

            entity.HasIndex(e => e.OrganizationId)
                .HasDatabaseName("IX_OrgRegSub_OrganizationId");

            entity.HasIndex(e => e.RegisterId)
                .HasDatabaseName("IX_OrgRegSub_RegisterId");

            // Hot path: RegisterSubscriptionService and RegisterInvitationService
            // both filter by (OrganizationId, Status='Active'). Partial keeps the
            // index narrow even as historical/cancelled subscriptions accumulate.
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"Status\" = 'Active'")
                .HasDatabaseName("IX_OrgRegSub_Org_Active");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureRegisterInvitations(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.IsInMemory();

        modelBuilder.Entity<RegisterInvitationRecord>(entity =>
        {
            if (isInMemory)
                entity.ToTable("RegisterInvitationRecords");
            else
                entity.ToTable("RegisterInvitationRecords", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.InvitationId)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.TargetOrgDid)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.TargetWalletAddress)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.RegisterId)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.RegisterName)
                .HasMaxLength(38);

            entity.Property(e => e.Nonce)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(16);

            entity.HasIndex(e => e.InvitationId)
                .IsUnique()
                .HasDatabaseName("IX_RegInvRec_InvitationId");

            entity.HasIndex(e => e.SourceOrgId)
                .HasDatabaseName("IX_RegInvRec_SourceOrgId");

            entity.HasIndex(e => e.TargetOrgDid)
                .HasDatabaseName("IX_RegInvRec_TargetOrgDid");

            entity.HasIndex(e => new { e.SourceOrgId, e.Status })
                .HasDatabaseName("IX_RegInvRec_SourceOrgId_Status");

            entity.HasOne(e => e.SourceOrganization)
                .WithMany()
                .HasForeignKey(e => e.SourceOrgId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvitationNonce>(entity =>
        {
            if (isInMemory)
                entity.ToTable("InvitationNonces");
            else
                entity.ToTable("InvitationNonces", "public");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Nonce)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.InvitationId)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.RegisterId)
                .IsRequired()
                .HasMaxLength(32);

            entity.HasIndex(e => e.Nonce)
                .IsUnique()
                .HasDatabaseName("IX_InvNonce_Nonce");
        });
    }
}
