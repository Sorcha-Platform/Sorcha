// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Per-register subscription state tracking replication mode, sync progress,
/// and source peers for a locally subscribed register.
/// </summary>
public class RegisterSubscription
{
    /// <summary>
    /// Unique identifier for this subscription (database key)
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Identifier of the register being subscribed to
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Admin-configured replication mode
    /// </summary>
    [Required]
    public ReplicationMode Mode { get; set; } = ReplicationMode.ForwardOnly;

    /// <summary>
    /// Current synchronization state
    /// </summary>
    [Required]
    public RegisterSyncState SyncState { get; set; } = RegisterSyncState.Subscribing;

    /// <summary>
    /// Last successfully synced docket version (for full replica: docket chain position).
    /// -1 means no dockets synced yet (genesis at version 0 will be included in first pull).
    /// </summary>
    public long LastSyncedDocketVersion { get; set; } = -1;

    /// <summary>
    /// Last successfully synced transaction version (incremental sync cursor)
    /// </summary>
    public long LastSyncedTransactionVersion { get; set; } = 0;

    /// <summary>
    /// Total number of dockets in the chain (known from remote peers)
    /// </summary>
    public long TotalDocketsInChain { get; set; } = 0;

    /// <summary>
    /// Peer IDs currently used as source for syncing this register
    /// </summary>
    public List<string> SourcePeerIds { get; set; } = new();

    /// <summary>
    /// When this subscription was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the last successful sync occurred
    /// </summary>
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>
    /// Error message if SyncState is Error
    /// </summary>
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of consecutive sync failures
    /// </summary>
    public int ConsecutiveFailures { get; set; } = 0;

    /// <summary>
    /// Whether this peer can participate in validation/docket building for this register.
    /// Only true when SyncState is FullyReplicated.
    /// </summary>
    public bool CanParticipateInValidation => SyncState == RegisterSyncState.FullyReplicated;

    /// <summary>
    /// Whether this subscription is actively receiving transactions
    /// </summary>
    public bool IsReceiving => SyncState is RegisterSyncState.FullyReplicated or RegisterSyncState.Active;

    /// <summary>
    /// Sync progress percentage (0-100) for full replica mode
    /// </summary>
    public double SyncProgressPercent
    {
        get
        {
            if (Mode == ReplicationMode.ForwardOnly)
                return SyncState == RegisterSyncState.Active ? 100.0 : 0.0;

            if (TotalDocketsInChain == 0)
                return 0.0;

            return Math.Min(100.0, (double)LastSyncedDocketVersion / TotalDocketsInChain * 100.0);
        }
    }

    /// <summary>
    /// Records a successful sync step
    /// </summary>
    public void RecordSyncSuccess(long docketVersion, long transactionVersion)
    {
        LastSyncedDocketVersion = docketVersion;
        LastSyncedTransactionVersion = transactionVersion;
        LastSyncAt = DateTimeOffset.UtcNow;
        ConsecutiveFailures = 0;
        ErrorMessage = null;
    }

    /// <summary>
    /// Records a sync failure
    /// </summary>
    public void RecordSyncFailure(string errorMessage)
    {
        ConsecutiveFailures++;
        ErrorMessage = errorMessage;

        if (ConsecutiveFailures >= PeerServiceConstants.MaxConsecutiveFailuresBeforeError)
        {
            SyncState = RegisterSyncState.Error;
        }
    }

    /// <summary>
    /// Transitions to the next state based on replication mode
    /// </summary>
    public void TransitionToNextState()
    {
        SyncState = (SyncState, Mode) switch
        {
            (RegisterSyncState.Subscribing, ReplicationMode.FullReplica) => RegisterSyncState.Syncing,
            (RegisterSyncState.Subscribing, ReplicationMode.ForwardOnly) => RegisterSyncState.Active,
            (RegisterSyncState.Syncing, _) => RegisterSyncState.FullyReplicated,
            _ => SyncState
        };
    }
}
