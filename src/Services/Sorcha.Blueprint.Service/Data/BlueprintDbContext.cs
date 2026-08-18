// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Blueprint.Service.Data.Entities;

namespace Sorcha.Blueprint.Service.Data;

/// <summary>
/// Entity Framework Core database context for Blueprint Service persistence.
/// Manages drafts, templates, actions, file metadata, and instance state.
/// </summary>
public class BlueprintDbContext : DbContext
{
    public BlueprintDbContext(DbContextOptions<BlueprintDbContext> options) : base(options)
    {
    }

    public DbSet<BlueprintDraftEntity> BlueprintDrafts => Set<BlueprintDraftEntity>();
    public DbSet<BlueprintDraftAccessEntity> BlueprintDraftAccess => Set<BlueprintDraftAccessEntity>();
    public DbSet<BlueprintTemplateEntity> BlueprintTemplates => Set<BlueprintTemplateEntity>();
    public DbSet<ActionEntity> Actions => Set<ActionEntity>();
    public DbSet<FileMetadataEntity> FileMetadata => Set<FileMetadataEntity>();
    public DbSet<InstanceEntity> Instances => Set<InstanceEntity>();

    /// <summary>
    /// Feature 142 — rehearsal-pass records backing the server-side publish soft gate.
    /// </summary>
    public DbSet<RehearsalPassEntity> RehearsalPasses => Set<RehearsalPassEntity>();

    /// <summary>
    /// Feature 142 — append-only audit of publishes that overrode the rehearsal gate.
    /// </summary>
    public DbSet<PublishOverrideEntity> PublishOverrides => Set<PublishOverrideEntity>();

    /// <summary>
    /// Bitstring Status Lists. Durable because revocation must survive a restart, and because the
    /// allocation counter cannot be reconstructed from the ledger (#1482).
    /// </summary>
    public DbSet<StatusListEntity> StatusLists => Set<StatusListEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("blueprint");

        // BlueprintDraft configuration
        modelBuilder.Entity<BlueprintDraftEntity>(entity =>
        {
            entity.ToTable("BlueprintDrafts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).HasColumnType("jsonb");
            entity.HasIndex(e => e.OwnerId).HasDatabaseName("IX_Drafts_OwnerId");
            entity.HasIndex(e => e.OrganizationId).HasDatabaseName("IX_Drafts_OrgId");
            entity.HasMany(e => e.AccessEntries)
                .WithOne(a => a.Draft)
                .HasForeignKey(a => a.DraftId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BlueprintDraftAccess configuration (schema-only placeholder)
        modelBuilder.Entity<BlueprintDraftAccessEntity>(entity =>
        {
            entity.ToTable("BlueprintDraftAccess");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DraftId, e.UserId }).IsUnique();
        });

        // BlueprintTemplate configuration
        modelBuilder.Entity<BlueprintTemplateEntity>(entity =>
        {
            entity.ToTable("BlueprintTemplates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).HasColumnType("jsonb");
            entity.HasIndex(e => e.Category).HasDatabaseName("IX_Templates_Category");
        });

        // Action configuration
        modelBuilder.Entity<ActionEntity>(entity =>
        {
            entity.ToTable("Actions");
            entity.HasKey(e => e.TransactionHash);
            entity.Property(e => e.Content).HasColumnType("jsonb");
            // perf audit F11: the by-wallet list query filters (WalletAddress, RegisterAddress) then
            // ORDER BY CreatedAt DESC — a sort-aligned composite serves filter + sort + page from one index
            // (the leading columns still serve the plain (WalletAddress, RegisterAddress) lookup).
            entity.HasIndex(e => new { e.WalletAddress, e.RegisterAddress, e.CreatedAt })
                .HasDatabaseName("IX_Actions_Wallet_Register_CreatedAt")
                .IsDescending(false, false, true);
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("UX_Actions_IdempotencyKey")
                .HasFilter("\"IdempotencyKey\" IS NOT NULL");
            entity.HasMany(e => e.Files)
                .WithOne(f => f.Action)
                .HasForeignKey(f => f.TransactionHash)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // FileMetadata configuration
        modelBuilder.Entity<FileMetadataEntity>(entity =>
        {
            entity.ToTable("FileMetadata");
            entity.HasKey(e => e.Id);

            // perf audit F14: the orphan-chunk sweep filters TransactionHash IS NULL AND CreatedAt < cutoff
            // (EfCoreActionStore). A partial index on CreatedAt over the null-TransactionHash subset keeps
            // the periodic sweep O(orphan working set) instead of scanning all file metadata.
            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_FileMetadata_Orphans")
                .HasFilter("\"TransactionHash\" IS NULL");
        });

        // Instance configuration
        modelBuilder.Entity<InstanceEntity>(entity =>
        {
            entity.ToTable("Instances");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CurrentActionIds).HasColumnType("jsonb");
            entity.Property(e => e.ParticipantWallets).HasColumnType("jsonb");
            entity.Property(e => e.AccumulatedData).HasColumnType("jsonb");
            entity.Property(e => e.PendingActionPayloads).HasColumnType("jsonb");
            entity.Property(e => e.ActiveBranches).HasColumnType("jsonb");
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.Property(e => e.Version).IsConcurrencyToken();
            // perf audit F11: list/history queries filter on these then ORDER BY CreatedAt/UpdatedAt DESC —
            // sort-aligned composites serve filter + sort + page from the index (the leading column still
            // serves the plain lookups the single-column indexes used to).
            entity.HasIndex(e => new { e.BlueprintId, e.CreatedAt })
                .HasDatabaseName("IX_Instances_BlueprintId_CreatedAt")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.RegisterId, e.CreatedAt })
                .HasDatabaseName("IX_Instances_RegisterId_CreatedAt")
                .IsDescending(false, true);
            entity.HasIndex(e => new { e.State, e.UpdatedAt })
                .HasDatabaseName("IX_Instances_State_UpdatedAt")
                .IsDescending(false, true);
        });

        // Feature 142 — RehearsalPass configuration
        modelBuilder.Entity<RehearsalPassEntity>(entity =>
        {
            entity.ToTable("RehearsalPasses");
            entity.HasKey(e => e.Id);
            // Supports "latest pass per (BlueprintId, ExecDefHash)" lookups; the
            // store orders by RehearsedAt descending within the matched set.
            entity.HasIndex(e => new { e.BlueprintId, e.ExecDefHash, e.RehearsedAt })
                .HasDatabaseName("IX_RehearsalPasses_Blueprint_ExecDef_RehearsedAt");
        });

        // Feature 142 — PublishOverride configuration (append-only audit)
        modelBuilder.Entity<StatusListEntity>(entity =>
        {
            entity.ToTable("StatusLists");
            entity.HasKey(e => e.Id);
            // Reconciliation and rebuild both work per issuer+register.
            entity.HasIndex(e => new { e.IssuerWallet, e.RegisterId, e.Purpose });
        });

        modelBuilder.Entity<PublishOverrideEntity>(entity =>
        {
            entity.ToTable("PublishOverrides");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.BlueprintId, e.OverriddenAt })
                .HasDatabaseName("IX_PublishOverrides_Blueprint_OverriddenAt");
        });
    }
}
