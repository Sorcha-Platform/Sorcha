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
            entity.HasIndex(e => new { e.WalletAddress, e.RegisterAddress })
                .HasDatabaseName("IX_Actions_Wallet_Register");
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
            entity.HasIndex(e => e.BlueprintId).HasDatabaseName("IX_Instances_BlueprintId");
            entity.HasIndex(e => e.RegisterId).HasDatabaseName("IX_Instances_RegisterId");
            entity.HasIndex(e => e.State).HasDatabaseName("IX_Instances_State");
        });
    }
}
