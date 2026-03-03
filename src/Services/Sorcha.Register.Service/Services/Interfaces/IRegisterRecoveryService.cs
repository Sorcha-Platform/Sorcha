// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Services.Interfaces;

/// <summary>
/// Orchestrates register recovery on startup.
/// Detects docket gaps between local state and the peer network head,
/// streams missing dockets, verifies chain integrity, runs bloom filter
/// checks on recovered transactions, and sends batch notifications.
/// Tracks recovery state in Redis for health endpoint monitoring.
/// </summary>
public interface IRegisterRecoveryService
{
    /// <summary>
    /// Check if recovery is needed for a register and execute if so.
    /// Compares local latest docket vs network head, enters recovery mode
    /// if gap detected.
    /// </summary>
    /// <param name="registerId">Register to check and recover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if recovery was needed and completed, false if already synced.</returns>
    Task<bool> RecoverIfNeededAsync(string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current recovery state for a register.
    /// </summary>
    /// <param name="registerId">Register to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current recovery state, or null if no state exists.</returns>
    Task<RecoveryState?> GetRecoveryStateAsync(string registerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Recovery state for a register, tracked in Redis hash.
/// </summary>
public record RecoveryState
{
    /// <summary>Register being recovered.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Current recovery status.</summary>
    public RecoveryStatus Status { get; init; } = RecoveryStatus.Synced;

    /// <summary>Last docket number stored locally.</summary>
    public long LocalLatestDocket { get; init; }

    /// <summary>Latest docket number on the network.</summary>
    public long NetworkHeadDocket { get; init; }

    /// <summary>When recovery started.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When last docket was processed.</summary>
    public DateTimeOffset LastProgressAt { get; init; }

    /// <summary>Count of dockets processed so far.</summary>
    public long DocketsProcessed { get; init; }

    /// <summary>Number of errors during recovery.</summary>
    public int ErrorCount { get; init; }

    /// <summary>Most recent error message.</summary>
    public string? LastError { get; init; }
}

/// <summary>
/// Recovery status enum.
/// </summary>
public enum RecoveryStatus
{
    /// <summary>Up to date with network.</summary>
    Synced = 0,

    /// <summary>Actively catching up.</summary>
    Recovering = 1,

    /// <summary>Recovery paused due to errors.</summary>
    Stalled = 2
}
