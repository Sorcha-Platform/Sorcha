// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Data;

/// <summary>
/// Entity Framework Core DbContext for Wallet persistence
/// Supports PostgreSQL as the primary database provider
/// </summary>
public class WalletDbContext : DbContext
{
    /// <summary>
    /// Wallets table
    /// </summary>
    public DbSet<Domain.Entities.Wallet> Wallets => Set<Domain.Entities.Wallet>();

    /// <summary>
    /// Derived addresses table
    /// </summary>
    public DbSet<WalletAddress> WalletAddresses => Set<WalletAddress>();

    /// <summary>
    /// Access control entries table
    /// </summary>
    public DbSet<WalletAccess> WalletAccess => Set<WalletAccess>();

    /// <summary>
    /// Transaction history table
    /// </summary>
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    /// <summary>
    /// Stored verifiable credentials
    /// </summary>
    public DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();

    /// <summary>
    /// Recovery key wraps (one per wallet per recovery path)
    /// </summary>
    public DbSet<RecoveryKeyWrap> RecoveryKeyWraps => Set<RecoveryKeyWrap>();

    /// <summary>
    /// Recovery audit log (immutable trail of recovery operations)
    /// </summary>
    public DbSet<RecoveryAuditLog> RecoveryAuditLogs => Set<RecoveryAuditLog>();

    /// <summary>
    /// Organisation master keys for HD key derivation
    /// </summary>
    public DbSet<OrgMasterKey> OrgMasterKeys => Set<OrgMasterKey>();

    /// <summary>
    /// Derived key records linking users to org-derived wallets
    /// </summary>
    public DbSet<DerivedKeyRecord> DerivedKeyRecords => Set<DerivedKeyRecord>();

    /// <summary>
    /// Threshold key groups (schema only — no service code)
    /// </summary>
    public DbSet<ThresholdKeyGroup> ThresholdKeyGroups => Set<ThresholdKeyGroup>();

    /// <summary>
    /// Signing key shares (schema only — no service code)
    /// </summary>
    public DbSet<SigningKeyShare> SigningKeyShares => Set<SigningKeyShare>();

    /// <summary>
    /// Signing sessions (schema only — no service code)
    /// </summary>
    public DbSet<SigningSession> SigningSessions => Set<SigningSession>();

    /// <summary>
    /// Per-org citizen device status lists (Feature 114).
    /// </summary>
    public DbSet<CitizenDeviceStatusList> CitizenDeviceStatusLists => Set<CitizenDeviceStatusList>();

    /// <summary>
    /// Per-(PlatformUser, device) sync cursors (Feature 114).
    /// </summary>
    public DbSet<CitizenWalletSyncCursor> CitizenWalletSyncCursors => Set<CitizenWalletSyncCursor>();

    /// <summary>
    /// Reverse map from citizen holder wallet address to PlatformUser (Feature 114, US4).
    /// </summary>
    public DbSet<CitizenHolderIndex> CitizenHolderIndex => Set<CitizenHolderIndex>();

    /// <summary>
    /// Append-only credential-lifecycle event log per citizen (Feature 114, US4).
    /// </summary>
    public DbSet<CitizenCredentialEventLog> CitizenCredentialEventLog => Set<CitizenCredentialEventLog>();

    /// <summary>
    /// Per-organisation VC issuance key lifecycle state (Feature 120 US2, data-model §4).
    /// </summary>
    public DbSet<IssuanceKeyState> IssuanceKeyStates => Set<IssuanceKeyState>();

    /// <summary>
    /// Durable per-citizen presentation history for cross-device Activity (Feature 114, US5 PR3).
    /// </summary>
    public DbSet<CitizenPresentationRecord> CitizenPresentationRecords => Set<CitizenPresentationRecord>();

    /// <summary>
    /// Initializes a new instance of the <see cref="WalletDbContext"/> class.
    /// </summary>
    /// <param name="options">The database context options configured with PostgreSQL connection string.</param>
    public WalletDbContext(DbContextOptions<WalletDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived test contexts (enables InMemory provider subclassing).
    /// </summary>
    protected WalletDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// Configures the database schema, entity relationships, indexes, and constraints.
    /// Sets up PostgreSQL-specific features including jsonb columns, soft delete filters, and optimistic concurrency.
    /// </summary>
    /// <param name="modelBuilder">The model builder used to configure the database schema.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure schema
        modelBuilder.HasDefaultSchema("wallet");

        ConfigureWallet(modelBuilder);
        ConfigureWalletAddress(modelBuilder);
        ConfigureWalletAccess(modelBuilder);
        ConfigureWalletTransaction(modelBuilder);
        ConfigureCredential(modelBuilder);
        ConfigureRecoveryKeyWrap(modelBuilder);
        ConfigureRecoveryAuditLog(modelBuilder);
        ConfigureOrgMasterKey(modelBuilder);
        ConfigureDerivedKeyRecord(modelBuilder);
        ConfigureThresholdKeyGroup(modelBuilder);
        ConfigureSigningKeyShare(modelBuilder);
        ConfigureSigningSession(modelBuilder);
        ConfigureCitizenDeviceStatusList(modelBuilder);
        ConfigureCitizenWalletSyncCursor(modelBuilder);
        ConfigureCitizenHolderIndex(modelBuilder);
        ConfigureCitizenCredentialEventLog(modelBuilder);
        ConfigureIssuanceKeyState(modelBuilder);
        ConfigureCitizenPresentationRecord(modelBuilder);
    }

    // Feature 114 US5 PR3: durable per-citizen presentation history. Composite PK
    // (PlatformUserId, EntryId) makes the forward upsert idempotent and scopes
    // reads/deletes to the owner; (PlatformUserId, PresentedAt desc) serves the
    // newest-first list. DisclosedClaims is jsonb (names only — never values).
    private static void ConfigureCitizenPresentationRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CitizenPresentationRecord>(entity =>
        {
            entity.ToTable("CitizenPresentationRecords");

            entity.HasKey(e => new { e.PlatformUserId, e.EntryId });

            entity.Property(e => e.PlatformUserId).IsRequired();
            entity.Property(e => e.EntryId).IsRequired();
            entity.Property(e => e.CredentialId).IsRequired();
            entity.Property(e => e.Outcome).IsRequired();
            entity.Property(e => e.PresentedAt).IsRequired();
            entity.Property(e => e.ReportedAt).IsRequired();

            entity.Property(e => e.VerifierLabel).HasMaxLength(200);
            entity.Property(e => e.VerifierDid).HasMaxLength(200);

            entity.Property(e => e.DisclosedClaims)
                .HasColumnType("jsonb")
                .IsRequired();

            entity.HasIndex(e => new { e.PlatformUserId, e.PresentedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_CitizenPresentationRecords_User_PresentedAt");
        });
    }

    private static void ConfigureIssuanceKeyState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IssuanceKeyState>(entity =>
        {
            entity.ToTable("IssuanceKeyStates");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.OrganizationId).IsRequired();
            entity.Property(e => e.Slot).IsRequired();
            entity.Property(e => e.RotationIndex).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>().IsRequired();
            entity.Property(e => e.PublicKey).IsRequired();
            entity.Property(e => e.Algorithm).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Thumbprint).IsRequired().HasMaxLength(43);
            entity.Property(e => e.DerivedAt).IsRequired();
            entity.Property(e => e.RevocationReason).HasMaxLength(500);

            // FR-016 — at most one Active key per (Org, Slot).
            entity.HasIndex(e => new { e.OrganizationId, e.Slot })
                .HasFilter("\"Status\" = 0")
                .IsUnique()
                .HasDatabaseName("IX_IssuanceKeyStates_Org_Slot_Active");

            // FR-011 — RotationIndex unique per Org.
            entity.HasIndex(e => new { e.OrganizationId, e.RotationIndex })
                .IsUnique()
                .HasDatabaseName("IX_IssuanceKeyStates_Org_Rotation");
        });
    }

    private static void ConfigureWallet(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Entities.Wallet>(entity =>
        {
            entity.ToTable("Wallets");

            // Primary key is the wallet address (unique identifier)
            entity.HasKey(e => e.Address);
            entity.Property(e => e.Address)
                .HasColumnType("text")
                .IsRequired()
                .HasComment("Wallet address in Bech32m format (ws1...). Variable length by algorithm: ED25519 ~66 chars, NISTP256 ~107 chars, RSA4096 ~700 chars.");

            // Required fields
            entity.Property(e => e.EncryptedPrivateKey)
                .IsRequired()
                .HasMaxLength(16384);

            entity.Property(e => e.EncryptionKeyId)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Owner)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Tenant)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(256);

            // Optional fields
            entity.Property(e => e.Description)
                .HasMaxLength(1024);

            entity.Property(e => e.PublicKey)
                .HasMaxLength(8192);

            // Enum conversion to string for readability
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(WalletStatus.Active);

            // JSON columns for dictionaries (PostgreSQL jsonb)
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");

            entity.Property(e => e.Tags)
                .HasColumnType("jsonb");

            // Timestamps
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.LastAccessedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Concurrency token for optimistic locking
            entity.Property(e => e.RowVersion)
                .IsRowVersion();

            // Indexes for common queries
            entity.HasIndex(e => e.Owner)
                .HasDatabaseName("IX_Wallets_Owner");

            entity.HasIndex(e => e.Tenant)
                .HasDatabaseName("IX_Wallets_Tenant");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_Wallets_Status");

            entity.HasIndex(e => new { e.Owner, e.Tenant })
                .HasDatabaseName("IX_Wallets_Owner_Tenant");

            entity.HasIndex(e => new { e.Tenant, e.Status })
                .HasDatabaseName("IX_Wallets_Tenant_Status");

            // Signing mode (Feature 082)
            entity.Property(e => e.SigningMode)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(SigningMode.Local);

            entity.Property(e => e.KmsKeyId)
                .HasMaxLength(512);

            // Recovery fields (Feature 060)
            entity.Property(e => e.EncryptedMasterKeyBlob)
                .HasMaxLength(16384);

            entity.Property(e => e.RecoveryEnabled)
                .HasDefaultValue(false);

            // Org key derivation fields (Feature 083)
            entity.Property(e => e.DerivedKeyRecordId);

            entity.Property(e => e.CustodyMode)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(CustodyMode.Custodial);

            entity.HasOne<DerivedKeyRecord>()
                .WithOne(d => d.Wallet)
                .HasForeignKey<Domain.Entities.Wallet>(e => e.DerivedKeyRecordId)
                .OnDelete(DeleteBehavior.SetNull);

            // HAIP issuer flag (Feature 094)
            entity.Property(e => e.HaipIssuer)
                .HasDefaultValue(false);

            // Soft delete filter
            entity.HasQueryFilter(e => e.DeletedAt == null);
        });
    }

    private static void ConfigureWalletAddress(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletAddress>(entity =>
        {
            entity.ToTable("WalletAddresses");

            // Primary key
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            // Required fields
            entity.Property(e => e.ParentWalletAddress)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.Address)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.DerivationPath)
                .IsRequired()
                .HasMaxLength(256);

            // Optional fields
            entity.Property(e => e.Label)
                .HasMaxLength(256);

            entity.Property(e => e.PublicKey)
                .HasMaxLength(8192);

            entity.Property(e => e.Notes)
                .HasMaxLength(2048);

            entity.Property(e => e.Tags)
                .HasMaxLength(1024);

            // JSON column for metadata
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");

            // Timestamps
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationship
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.Addresses)
                .HasForeignKey(e => e.ParentWalletAddress)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.ParentWalletAddress)
                .HasDatabaseName("IX_WalletAddresses_ParentWalletAddress");

            entity.HasIndex(e => e.Address)
                .IsUnique()
                .HasDatabaseName("IX_WalletAddresses_Address");

            entity.HasIndex(e => new { e.ParentWalletAddress, e.Index })
                .HasDatabaseName("IX_WalletAddresses_Parent_Index");

            entity.HasIndex(e => new { e.ParentWalletAddress, e.Account, e.IsChange, e.Index })
                .HasDatabaseName("IX_WalletAddresses_Derivation");
        });
    }

    private static void ConfigureWalletAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletAccess>(entity =>
        {
            entity.ToTable("WalletAccess");

            // Primary key
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            // Required fields
            entity.Property(e => e.ParentWalletAddress)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.Subject)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.GrantedBy)
                .IsRequired()
                .HasMaxLength(256);

            // Enum conversion to string
            entity.Property(e => e.AccessRight)
                .HasConversion<string>()
                .HasMaxLength(50);

            // Optional fields
            entity.Property(e => e.Reason)
                .HasMaxLength(1024);

            entity.Property(e => e.RevokedBy)
                .HasMaxLength(256);

            // Timestamps
            entity.Property(e => e.GrantedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationship
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.Delegates)
                .HasForeignKey(e => e.ParentWalletAddress)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.ParentWalletAddress)
                .HasDatabaseName("IX_WalletAccess_ParentWalletAddress");

            entity.HasIndex(e => e.Subject)
                .HasDatabaseName("IX_WalletAccess_Subject");

            entity.HasIndex(e => new { e.ParentWalletAddress, e.Subject })
                .HasDatabaseName("IX_WalletAccess_Parent_Subject");

            // Index for finding active access entries
            entity.HasIndex(e => new { e.Subject, e.RevokedAt })
                .HasDatabaseName("IX_WalletAccess_Subject_Active");

            // Ignore computed property
            entity.Ignore(e => e.IsActive);
        });
    }

    private static void ConfigureWalletTransaction(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.ToTable("WalletTransactions");

            // Primary key
            entity.HasKey(e => e.TransactionId);
            entity.Property(e => e.TransactionId)
                .HasMaxLength(256)
                .IsRequired();

            // Required fields
            entity.Property(e => e.ParentWalletAddress)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.TransactionType)
                .IsRequired()
                .HasMaxLength(50);

            // Enum conversion to string
            entity.Property(e => e.State)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(TransactionState.Pending);

            // Optional fields
            entity.Property(e => e.Amount)
                .HasPrecision(28, 18); // High precision for crypto amounts

            entity.Property(e => e.RawTransaction)
                .HasColumnType("text");

            // Transaction lifecycle fields
            entity.Property(e => e.RegisterId)
                .HasMaxLength(256);

            entity.Property(e => e.ReceiptId)
                .HasMaxLength(256);

            entity.Property(e => e.CounterpartyAddress)
                .HasColumnType("text");

            entity.Property(e => e.Direction)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(TransactionDirection.Outbound);

            // JSON column for metadata
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            // Timestamps
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Foreign key relationship
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.Transactions)
                .HasForeignKey(e => e.ParentWalletAddress)
                .OnDelete(DeleteBehavior.Cascade);

            // Add query filter to match parent Wallet's soft delete filter
            // This prevents loading transactions for soft-deleted wallets
            entity.HasQueryFilter(e => e.Wallet!.DeletedAt == null);

            // Indexes
            entity.HasIndex(e => e.ParentWalletAddress)
                .HasDatabaseName("IX_WalletTransactions_ParentWalletAddress");

            entity.HasIndex(e => e.State)
                .HasDatabaseName("IX_WalletTransactions_State");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_WalletTransactions_CreatedAt");

            entity.HasIndex(e => new { e.ParentWalletAddress, e.State })
                .HasDatabaseName("IX_WalletTransactions_Parent_State");

            entity.HasIndex(e => new { e.ParentWalletAddress, e.CreatedAt })
                .HasDatabaseName("IX_WalletTransactions_Parent_CreatedAt")
                .IsDescending(false, true);
        });
    }

    private static void ConfigureCredential(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CredentialEntity>(entity =>
        {
            entity.ToTable("Credentials");

            // Feature 106 fix — composite PK (Id, WalletAddress). A single credential
            // lives in BOTH issuer and recipient wallets (issuer: audit/Active; recipient:
            // PendingAcceptance → Active/Declined). On single-node deployments both rows
            // land in the same DB; the prior Id-only PK blocked the detector's recipient
            // insert with a PK conflict and the `already stored` dedup short-circuit
            // masked the failure.
            entity.HasKey(e => new { e.Id, e.WalletAddress });

            entity.Property(e => e.Id)
                .HasMaxLength(500);

            entity.Property(e => e.Type)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.IssuerDid)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.SubjectDid)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.ClaimsJson)
                .IsRequired()
                .HasColumnType("jsonb");

            entity.Property(e => e.RawToken)
                .IsRequired();

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(50)
                .HasConversion<string>()
                .HasDefaultValue(CredentialStatus.Active);

            entity.Property(e => e.IssuanceTxId)
                .HasMaxLength(200);

            entity.Property(e => e.IssuerOrgName)
                .HasMaxLength(200);

            entity.Property(e => e.IssuanceBlueprintId)
                .HasMaxLength(200);

            entity.Property(e => e.IssuanceInstanceId)
                .HasMaxLength(200);

            entity.Property(e => e.IssuanceActionId)
                .HasMaxLength(50);

            entity.Property(e => e.ClaimActionId)
                .HasMaxLength(50);

            entity.Property(e => e.RegisterId)
                .HasMaxLength(200);

            entity.Property(e => e.WalletAddress)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()");

            // Indexes
            entity.HasIndex(e => e.WalletAddress)
                .HasDatabaseName("IX_Credentials_WalletAddress");

            entity.HasIndex(e => e.Type)
                .HasDatabaseName("IX_Credentials_Type");

            entity.HasIndex(e => new { e.WalletAddress, e.Type })
                .HasDatabaseName("IX_Credentials_Wallet_Type");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_Credentials_Status");

            entity.HasIndex(e => e.IssuerDid)
                .HasDatabaseName("IX_Credentials_IssuerDid");
        });
    }

    private static void ConfigureRecoveryKeyWrap(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecoveryKeyWrap>(entity =>
        {
            entity.ToTable("RecoveryKeyWraps");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.WalletAddress)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.RecoveryPath)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.EncryptedRecoveryKey)
                .IsRequired()
                .HasMaxLength(8192);

            entity.Property(e => e.RecipientKeyId)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.RecoveryKeyWraps)
                .HasForeignKey(e => e.WalletAddress)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.WalletAddress)
                .HasDatabaseName("IX_RecoveryKeyWraps_WalletAddress");

            entity.HasIndex(e => e.RecipientKeyId)
                .HasDatabaseName("IX_RecoveryKeyWraps_RecipientKeyId");

            entity.HasIndex(e => new { e.WalletAddress, e.RecoveryPath })
                .HasDatabaseName("IX_RecoveryKeyWraps_Wallet_Path");
        });
    }

    private static void ConfigureRecoveryAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecoveryAuditLog>(entity =>
        {
            entity.ToTable("RecoveryAuditLogs");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.UserId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.TenantId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.RecoveryPath)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.InitiatedBy)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.IpAddress)
                .HasMaxLength(45);

            entity.Property(e => e.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_RecoveryAuditLogs_UserId");

            entity.HasIndex(e => new { e.UserId, e.Timestamp })
                .HasDatabaseName("IX_RecoveryAuditLogs_User_Timestamp");
        });
    }

    private static void ConfigureOrgMasterKey(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrgMasterKey>(entity =>
        {
            entity.ToTable("OrgMasterKeys");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.OrganizationId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.EncryptedSeed)
                .IsRequired();

            entity.Property(e => e.ProtectionProvider)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ProtectionKeyId)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("ED25519");

            entity.Property(e => e.MasterPublicKey)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(OrgMasterKeyStatus.Active);

            entity.Property(e => e.CreatedBy)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.OrganizationId)
                .IsUnique()
                .HasDatabaseName("IX_OrgMasterKeys_OrganizationId");
        });
    }

    private static void ConfigureDerivedKeyRecord(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DerivedKeyRecord>(entity =>
        {
            entity.ToTable("DerivedKeyRecords");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.OrganizationId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.UserId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.KeyUsage)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.DerivationPath)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.WalletAddress)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(DerivedKeyStatus.Active);

            entity.Property(e => e.CustodyMode)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(CustodyMode.Custodial);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // FK to OrgMasterKey
            entity.HasOne(e => e.OrgMasterKey)
                .WithMany(m => m.DerivedKeys)
                .HasForeignKey(e => e.OrgMasterKeyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique composite: one derivation per (master, user, dept, usage, index)
            entity.HasIndex(e => new { e.OrgMasterKeyId, e.UserId, e.DepartmentId, e.KeyUsage, e.KeyIndex })
                .IsUnique()
                .HasDatabaseName("IX_DerivedKeyRecords_Unique_Path");

            entity.HasIndex(e => new { e.OrganizationId, e.UserId })
                .HasDatabaseName("IX_DerivedKeyRecords_Org_User");

            entity.HasIndex(e => e.WalletAddress)
                .HasDatabaseName("IX_DerivedKeyRecords_WalletAddress");
        });
    }

    private static void ConfigureThresholdKeyGroup(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThresholdKeyGroup>(entity =>
        {
            entity.ToTable("ThresholdKeyGroups");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.GroupPublicKey)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Algorithm)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.DkgSessionId)
                .HasMaxLength(256);

            entity.Property(e => e.OrganizationId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(ThresholdKeyGroupStatus.Pending);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.OrganizationId)
                .HasDatabaseName("IX_ThresholdKeyGroups_OrganizationId");
        });
    }

    private static void ConfigureSigningKeyShare(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SigningKeyShare>(entity =>
        {
            entity.ToTable("SigningKeyShares");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.ParticipantId)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.EncryptedShareData)
                .IsRequired();

            entity.Property(e => e.ProtectionKeyId)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(ThresholdKeyGroupStatus.Active);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.ThresholdKeyGroup)
                .WithMany(g => g.Shares)
                .HasForeignKey(e => e.ThresholdKeyGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ThresholdKeyGroupId, e.ShareIndex })
                .IsUnique()
                .HasDatabaseName("IX_SigningKeyShares_Group_Index");
        });
    }

    private static void ConfigureSigningSession(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SigningSession>(entity =>
        {
            entity.ToTable("SigningSessions");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.TransactionId)
                .HasMaxLength(256);

            entity.Property(e => e.State)
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(SigningSessionState.Initializing);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.ThresholdKeyGroup)
                .WithMany(g => g.Sessions)
                .HasForeignKey(e => e.ThresholdKeyGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ThresholdKeyGroupId)
                .HasDatabaseName("IX_SigningSessions_ThresholdKeyGroupId");
        });
    }

    // Feature 114: Citizen wallet device status list — one or more per org,
    // unique on (OrganizationId, ListId), bitstring stored as bytea.
    private static void ConfigureCitizenDeviceStatusList(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CitizenDeviceStatusList>(entity =>
        {
            entity.ToTable("CitizenDeviceStatusLists");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.OrganizationId).IsRequired();
            entity.Property(e => e.ListId).IsRequired();
            entity.Property(e => e.Capacity).IsRequired();

            entity.Property(e => e.Bitstring)
                .HasColumnType("bytea")
                .IsRequired();

            entity.Property(e => e.RevokedCount).IsRequired();
            entity.Property(e => e.LastAllocatedIndex).IsRequired();
            entity.Property(e => e.GeneratedAt).IsRequired();
            entity.Property(e => e.ExpiresAt).IsRequired();

            entity.Property(e => e.SignedJwt)
                .HasColumnType("text")
                .IsRequired();

            entity.HasIndex(e => new { e.OrganizationId, e.ListId })
                .IsUnique()
                .HasDatabaseName("IX_CitizenDeviceStatusLists_Org_ListId");
        });
    }

    // Feature 114: Citizen wallet sync cursor — one row per (PlatformUser, device).
    private static void ConfigureCitizenWalletSyncCursor(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CitizenWalletSyncCursor>(entity =>
        {
            entity.ToTable("CitizenWalletSyncCursors");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.PlatformUserId).IsRequired();
            entity.Property(e => e.PlatformUserDeviceId).IsRequired();
            entity.Property(e => e.LastEventSeq).IsRequired();
            entity.Property(e => e.LastSyncAt).IsRequired();

            entity.HasIndex(e => new { e.PlatformUserId, e.PlatformUserDeviceId })
                .IsUnique()
                .HasDatabaseName("IX_CitizenWalletSyncCursors_User_Device");
        });
    }

    // Feature 114 US4: reverse index from citizen holder wallet address → PlatformUser.
    // Populated on first holder-key derivation; consumed by IHolderAddressLookup.
    private static void ConfigureCitizenHolderIndex(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CitizenHolderIndex>(entity =>
        {
            entity.ToTable("CitizenHolderIndex");

            // WalletAddress is the natural PK — one holder address ↔ one citizen.
            entity.HasKey(e => e.WalletAddress);

            entity.Property(e => e.WalletAddress)
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.PlatformUserId).IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.PlatformUserId)
                .HasDatabaseName("IX_CitizenHolderIndex_PlatformUserId");
        });
    }

    // Feature 114 US4: append-only credential-lifecycle event log per citizen.
    // Backs ICitizenCredentialEventStream; (PlatformUserId, Seq) supports the
    // delta-read query the sync service issues on every wallet open.
    private static void ConfigureCitizenCredentialEventLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CitizenCredentialEventLog>(entity =>
        {
            entity.ToTable("CitizenCredentialEventLog");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.PlatformUserId).IsRequired();
            entity.Property(e => e.Seq).IsRequired();
            entity.Property(e => e.Kind).IsRequired();

            entity.Property(e => e.CredentialId)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Hot-path read index — citizens always query "events strictly newer than Seq".
            entity.HasIndex(e => new { e.PlatformUserId, e.Seq })
                .IsUnique()
                .HasDatabaseName("IX_CitizenCredentialEventLog_User_Seq");
        });
    }
}
