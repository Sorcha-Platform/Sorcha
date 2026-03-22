// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// Records a validator state transition for audit trail purposes.
/// Persisted to MongoDB for durable, queryable audit history.
/// </summary>
public record ValidatorAuditEntry
{
    /// <summary>Unique audit entry identifier</summary>
    public required string Id { get; init; }

    /// <summary>Register scope</summary>
    public required string RegisterId { get; init; }

    /// <summary>Validator affected by the state change</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Status before the transition</summary>
    public required ValidatorStatus PreviousStatus { get; init; }

    /// <summary>Status after the transition</summary>
    public required ValidatorStatus NewStatus { get; init; }

    /// <summary>Administrator wallet address who performed the action</summary>
    public required string PerformedBy { get; init; }

    /// <summary>Optional reason or notes for the transition</summary>
    public string? Reason { get; init; }

    /// <summary>When the transition occurred</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
