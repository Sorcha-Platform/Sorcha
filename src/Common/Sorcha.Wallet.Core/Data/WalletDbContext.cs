// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;

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

            entity.HasKey(e => e.Id);

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
                .HasDefaultValue("Active");

            entity.Property(e => e.IssuanceTxId)
                .HasMaxLength(200);

            entity.Property(e => e.IssuanceBlueprintId)
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
}
