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
    public DbSet<PublicIdentity> PublicIdentities => Set<PublicIdentity>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();
    public DbSet<SocialLoginLink> SocialLoginLinks => Set<SocialLoginLink>();
    public DbSet<ServicePrincipal> ServicePrincipals => Set<ServicePrincipal>();

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

        // Configure PublicIdentity entity
        ConfigurePublicIdentity(modelBuilder);

        // Configure PasskeyCredential entity (public schema)
        ConfigurePasskeyCredential(modelBuilder);

        // Configure SocialLoginLink entity (public schema)
        ConfigureSocialLoginLink(modelBuilder);

        // Configure ServicePrincipal entity
        ConfigureServicePrincipal(modelBuilder);

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

            // One-to-one relationship with IdentityProviderConfiguration
            entity.HasOne(e => e.IdentityProvider)
                .WithOne(i => i.Organization)
                .HasForeignKey<IdentityProviderConfiguration>(i => i.OrganizationId)
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

            entity.HasIndex(e => e.OrganizationId)
                .IsUnique();

            entity.HasIndex(e => e.ProviderPreset);
        });
    }

    private void ConfigurePublicIdentity(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<PublicIdentity>(entity =>
        {
            if (isInMemory)
                entity.ToTable("PublicIdentities");
            else
                entity.ToTable("PublicIdentities", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DisplayName)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Email)
                .IsRequired(false)
                .HasMaxLength(320);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.EmailVerified)
                .IsRequired();

            entity.HasIndex(e => e.Email);

            // PasskeyCredentials uses polymorphic OwnerId (points to PublicIdentity.Id or UserIdentity.Id).
            // No EF FK constraint — lookups use the composite index on (OwnerType, OwnerId).
            entity.Ignore(e => e.PasskeyCredentials);

            entity.HasMany(e => e.SocialLoginLinks)
                .WithOne(e => e.PublicIdentity)
                .HasForeignKey(e => e.PublicIdentityId)
                .OnDelete(DeleteBehavior.Cascade);
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

            entity.Property(e => e.OwnerType)
                .IsRequired()
                .HasMaxLength(20);

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

            entity.HasIndex(e => new { e.OwnerType, e.OwnerId })
                .HasDatabaseName("IX_PasskeyCredential_Owner");

            entity.HasIndex(e => new { e.OwnerId, e.Status })
                .HasDatabaseName("IX_PasskeyCredential_OwnerId_Status");

            entity.HasIndex(e => e.OrganizationId)
                .HasDatabaseName("IX_PasskeyCredential_OrgId");
        });
    }

    private void ConfigureSocialLoginLink(ModelBuilder modelBuilder)
    {
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                      || Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<SocialLoginLink>(entity =>
        {
            if (isInMemory)
                entity.ToTable("SocialLoginLinks");
            else
                entity.ToTable("SocialLoginLinks", "public");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProviderType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ExternalSubjectId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.LinkedEmail)
                .IsRequired(false)
                .HasMaxLength(320);

            entity.Property(e => e.DisplayName)
                .IsRequired(false)
                .HasMaxLength(256);

            entity.HasIndex(e => new { e.ProviderType, e.ExternalSubjectId })
                .IsUnique()
                .HasDatabaseName("UQ_SocialLogin_Provider_Subject");

            entity.HasIndex(e => e.PublicIdentityId)
                .HasDatabaseName("IX_SocialLogin_PublicIdentityId");
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

            // ExternalIdpSubject is nullable (null for local auth users)
            entity.Property(e => e.ExternalIdpSubject)
                .IsRequired(false)
                .HasMaxLength(200);

            // PasswordHash is nullable (null for external IDP users)
            entity.Property(e => e.PasswordHash)
                .IsRequired(false)
                .HasMaxLength(500);

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

            // Unique index on ExternalIdpSubject only for non-null values
            entity.HasIndex(e => e.ExternalIdpSubject)
                .IsUnique()
                .HasFilter("\"ExternalIdpSubject\" IS NOT NULL");

            // New fields for org identity management
            entity.Property(e => e.VerificationToken).HasMaxLength(100);
            entity.Property(e => e.ProvisionedVia).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(e => e.Email)
                .IsUnique();  // Email must be unique within organization

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
}
