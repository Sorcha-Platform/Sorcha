// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Test DbContext that maps only the Feature 114 citizen entities for in-memory tests,
/// avoiding the Npgsql-specific jsonb / soft-delete configuration on the existing
/// wallet entity graph (which is incompatible with EF Core's InMemory provider).
/// </summary>
internal sealed class TestCitizenWalletDbContext : WalletDbContext
{
    public TestCitizenWalletDbContext(DbContextOptions<TestCitizenWalletDbContext> options)
        : base((DbContextOptions)options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ignore every existing wallet entity — the WalletDbContext base class exposes
        // them via DbSet<T> properties and EF would otherwise scan their Npgsql-specific
        // configuration (jsonb columns, owned-collection JSON Tags, RowVersion, etc.).
        modelBuilder.Ignore<Sorcha.Wallet.Core.Domain.Entities.Wallet>();
        modelBuilder.Ignore<WalletAddress>();
        modelBuilder.Ignore<WalletAccess>();
        modelBuilder.Ignore<WalletTransaction>();
        modelBuilder.Ignore<CredentialEntity>();
        modelBuilder.Ignore<RecoveryKeyWrap>();
        modelBuilder.Ignore<RecoveryAuditLog>();
        modelBuilder.Ignore<OrgMasterKey>();
        modelBuilder.Ignore<DerivedKeyRecord>();
        modelBuilder.Ignore<ThresholdKeyGroup>();
        modelBuilder.Ignore<SigningKeyShare>();
        modelBuilder.Ignore<SigningSession>();

        // Map only the Feature 114 citizen entities.
        modelBuilder.Entity<CitizenDeviceStatusList>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.OrganizationId, e.ListId }).IsUnique();
        });

        modelBuilder.Entity<CitizenWalletSyncCursor>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PlatformUserId, e.PlatformUserDeviceId }).IsUnique();
        });
    }
}
