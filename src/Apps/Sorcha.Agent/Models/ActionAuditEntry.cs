// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Models;

/// <summary>
/// An audit log entry recording a decision made on an action.
/// </summary>
public record ActionAuditEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string ActionId { get; init; }
    public required string ActionName { get; init; }
    public required string Decision { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long DurationMs { get; init; }
}
