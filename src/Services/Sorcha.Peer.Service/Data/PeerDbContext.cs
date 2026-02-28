// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Peer.Service.Core;

namespace Sorcha.Peer.Service.Data;

/// <summary>
/// Entity Framework Core DbContext for the Peer Service.
/// Stores peer nodes, register subscriptions, and sync checkpoints.
/// </summary>
public class PeerDbContext : DbContext
{
    public DbSet<PeerNodeEntity> Peers => Set<PeerNodeEntity>();
    public DbSet<RegisterSubscriptionEntity> RegisterSubscriptions => Set<RegisterSubscriptionEntity>();
    public DbSet<SyncCheckpointEntity> SyncCheckpoints => Set<SyncCheckpointEntity>();

    public PeerDbContext(DbContextOptions<PeerDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
        if (!isInMemory)
        {
            modelBuilder.HasDefaultSchema("peer");
        }

        ConfigurePeerNode(modelBuilder);
        ConfigureRegisterSubscription(modelBuilder);
        ConfigureSyncCheckpoint(modelBuilder);
    }

    private static void ConfigurePeerNode(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PeerNodeEntity>(entity =>
        {
            entity.HasKey(e => e.PeerId);

            entity.Property(e => e.PeerId).HasMaxLength(64);
            entity.Property(e => e.Address).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Port).IsRequired();
            entity.Property(e => e.Protocols).HasMaxLength(500);
            entity.Property(e => e.IsSeedNode).HasDefaultValue(false);
            entity.Property(e => e.FailureCount).HasDefaultValue(0);
            entity.Property(e => e.AverageLatencyMs).HasDefaultValue(0);
            entity.Property(e => e.IsBanned).HasDefaultValue(false);
            entity.Property(e => e.BanReason).HasMaxLength(500);

            entity.HasIndex(e => e.IsSeedNode).HasDatabaseName("IX_Peers_IsSeedNode");
            entity.HasIndex(e => e.IsBanned).HasDatabaseName("IX_Peers_IsBanned");
            entity.HasIndex(e => e.LastSeen).HasDatabaseName("IX_Peers_LastSeen");
        });
    }

    private static void ConfigureRegisterSubscription(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisterSubscriptionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RegisterId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Mode).IsRequired();
            entity.Property(e => e.SyncState).IsRequired();
            entity.Property(e => e.SourcePeerIds).HasMaxLength(2000);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

            entity.HasIndex(e => e.RegisterId).IsUnique().HasDatabaseName("IX_RegisterSubscriptions_RegisterId");
            entity.HasIndex(e => e.SyncState).HasDatabaseName("IX_RegisterSubscriptions_SyncState");
        });
    }

    private static void ConfigureSyncCheckpoint(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncCheckpointEntity>(entity =>
        {
            entity.HasKey(e => new { e.PeerId, e.RegisterId });

            entity.Property(e => e.PeerId).HasMaxLength(64);
            entity.Property(e => e.RegisterId).HasMaxLength(255);
            entity.Property(e => e.SourcePeerId).HasMaxLength(64);
        });
    }
}

/// <summary>
/// Database entity for a peer node
/// </summary>
public class PeerNodeEntity
{
    public string PeerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocols { get; set; } = string.Empty;
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public int FailureCount { get; set; } = 0;
    public bool IsSeedNode { get; set; } = false;
    public int AverageLatencyMs { get; set; } = 0;
    public bool IsBanned { get; set; } = false;
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }

    public PeerNode ToDomain()
    {
        return new PeerNode
        {
            PeerId = PeerId,
            Address = Address,
            Port = Port,
            SupportedProtocols = string.IsNullOrEmpty(Protocols)
                ? new List<string>()
                : Protocols.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            FirstSeen = FirstSeen,
            LastSeen = LastSeen,
            FailureCount = FailureCount,
            IsSeedNode = IsSeedNode,
            AverageLatencyMs = AverageLatencyMs,
            IsBanned = IsBanned,
            BannedAt = BannedAt,
            BanReason = BanReason
        };
    }

    public static PeerNodeEntity FromDomain(PeerNode peer)
    {
        return new PeerNodeEntity
        {
            PeerId = peer.PeerId,
            Address = peer.Address,
            Port = peer.Port,
            Protocols = string.Join(",", peer.SupportedProtocols),
            FirstSeen = peer.FirstSeen,
            LastSeen = peer.LastSeen,
            FailureCount = peer.FailureCount,
            IsSeedNode = peer.IsSeedNode,
            AverageLatencyMs = peer.AverageLatencyMs,
            IsBanned = peer.IsBanned,
            BannedAt = peer.BannedAt,
            BanReason = peer.BanReason
        };
    }
}

/// <summary>
/// Database entity for a register subscription
/// </summary>
public class RegisterSubscriptionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RegisterId { get; set; } = string.Empty;
    public ReplicationMode Mode { get; set; } = ReplicationMode.ForwardOnly;
    public RegisterSyncState SyncState { get; set; } = RegisterSyncState.Subscribing;
    public long LastSyncedDocketVersion { get; set; } = 0;
    public long LastSyncedTransactionVersion { get; set; } = 0;
    public long TotalDocketsInChain { get; set; } = 0;
    public string SourcePeerIds { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int ConsecutiveFailures { get; set; } = 0;

    public RegisterSubscription ToDomain()
    {
        return new RegisterSubscription
        {
            Id = Id,
            RegisterId = RegisterId,
            Mode = Mode,
            SyncState = SyncState,
            LastSyncedDocketVersion = LastSyncedDocketVersion,
            LastSyncedTransactionVersion = LastSyncedTransactionVersion,
            TotalDocketsInChain = TotalDocketsInChain,
            SourcePeerIds = string.IsNullOrEmpty(SourcePeerIds)
                ? new List<string>()
                : SourcePeerIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            CreatedAt = CreatedAt,
            LastSyncAt = LastSyncAt,
            ErrorMessage = ErrorMessage,
            ConsecutiveFailures = ConsecutiveFailures
        };
    }

    public static RegisterSubscriptionEntity FromDomain(RegisterSubscription sub)
    {
        return new RegisterSubscriptionEntity
        {
            Id = sub.Id,
            RegisterId = sub.RegisterId,
            Mode = sub.Mode,
            SyncState = sub.SyncState,
            LastSyncedDocketVersion = sub.LastSyncedDocketVersion,
            LastSyncedTransactionVersion = sub.LastSyncedTransactionVersion,
            TotalDocketsInChain = sub.TotalDocketsInChain,
            SourcePeerIds = string.Join(",", sub.SourcePeerIds),
            CreatedAt = sub.CreatedAt,
            LastSyncAt = sub.LastSyncAt,
            ErrorMessage = sub.ErrorMessage,
            ConsecutiveFailures = sub.ConsecutiveFailures
        };
    }
}

/// <summary>
/// Database entity for a sync checkpoint
/// </summary>
public class SyncCheckpointEntity
{
    public string PeerId { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public long CurrentVersion { get; set; } = 0;
    public long LastSyncTime { get; set; }
    public int TotalItems { get; set; } = 0;
    public string? SourcePeerId { get; set; }
    public DateTime NextSyncDue { get; set; } = DateTime.UtcNow;
}
