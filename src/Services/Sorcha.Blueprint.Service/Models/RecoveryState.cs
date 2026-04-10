// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Tracks the overall recovery progress of the Blueprint Service on startup.
/// Registered as a singleton — the recovery service writes, health check reads.
/// </summary>
public class RecoveryState
{
    /// <summary>True when initial recovery finishes for all reachable registers.</summary>
    public bool IsComplete { get; set; }

    /// <summary>When recovery began.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When recovery finished (null if in progress).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When the last periodic refresh completed (null if never refreshed).</summary>
    public DateTimeOffset? LastRefreshedAt { get; set; }

    /// <summary>Per-register recovery status, keyed by register ID.</summary>
    public ConcurrentDictionary<string, RegisterRecoveryState> RegisterStates { get; } = new();
}

/// <summary>
/// Per-register recovery and health tracking.
/// </summary>
public class RegisterRecoveryState
{
    /// <summary>Register identifier.</summary>
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>Human-readable register name.</summary>
    public string RegisterName { get; set; } = string.Empty;

    /// <summary>Current health status.</summary>
    public RegisterHealthStatus Status { get; set; } = RegisterHealthStatus.Unknown;

    /// <summary>Transaction count from last successful query.</summary>
    public int Height { get; set; }

    /// <summary>When last health check ran.</summary>
    public DateTimeOffset LastCheckedAt { get; set; }

    /// <summary>When last successful query completed.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }

    /// <summary>Number of consecutive failed checks (0 if healthy).</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Number of published blueprints recovered from this register.</summary>
    public int RecoveredBlueprintCount { get; set; }

    /// <summary>Last error message if offline.</summary>
    public string? ErrorMessage { get; set; }
}
