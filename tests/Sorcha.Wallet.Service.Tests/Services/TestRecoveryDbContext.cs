// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Test DbContext that avoids Npgsql-specific jsonb column mappings
/// incompatible with the EF Core InMemory provider. Configures only
/// the entities needed for recovery tests.
/// </summary>
internal class TestRecoveryDbContext : WalletDbContext
{
    public TestRecoveryDbContext(DbContextOptions<TestRecoveryDbContext> options)
        : base((DbContextOptions)options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Minimal Wallet config without jsonb
        modelBuilder.Entity<Sorcha.Wallet.Core.Domain.Entities.Wallet>(entity =>
        {
            entity.HasKey(e => e.Address);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Ignore(e => e.Metadata);
            entity.Ignore(e => e.Tags);
            entity.Ignore(e => e.RowVersion);
        });

        modelBuilder.Entity<WalletAddress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.Metadata);
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.Addresses)
                .HasForeignKey(e => e.ParentWalletAddress);
        });

        modelBuilder.Entity<WalletAccess>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AccessRight).HasConversion<string>();
            entity.Ignore(e => e.IsActive);
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.Delegates)
                .HasForeignKey(e => e.ParentWalletAddress);
        });

        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId);
            entity.Property(e => e.State).HasConversion<string>();
            entity.Ignore(e => e.Metadata);
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.Transactions)
                .HasForeignKey(e => e.ParentWalletAddress);
        });

        modelBuilder.Entity<RecoveryKeyWrap>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RecoveryPath).HasConversion<string>();
            entity.HasOne(e => e.Wallet)
                .WithMany(w => w.RecoveryKeyWraps)
                .HasForeignKey(e => e.WalletAddress);
        });

        modelBuilder.Entity<RecoveryAuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RecoveryPath).HasConversion<string>();
        });

        modelBuilder.Entity<CredentialEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Feature 083: OrgMasterKey configuration
        modelBuilder.Entity<OrgMasterKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>();
            entity.HasMany(e => e.DerivedKeys)
                .WithOne(d => d.OrgMasterKey)
                .HasForeignKey(d => d.OrgMasterKeyId);
        });

        // Feature 083: Configure one-to-one relationship for DerivedKeyRecord
        modelBuilder.Entity<DerivedKeyRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.KeyUsage).HasConversion<string>();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.CustodyMode).HasConversion<string>();
        });

        modelBuilder.Entity<Sorcha.Wallet.Core.Domain.Entities.Wallet>()
            .HasOne<DerivedKeyRecord>()
            .WithOne(d => d.Wallet)
            .HasForeignKey<Sorcha.Wallet.Core.Domain.Entities.Wallet>(e => e.DerivedKeyRecordId);
    }
}
