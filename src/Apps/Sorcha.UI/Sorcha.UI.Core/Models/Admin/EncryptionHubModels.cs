// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// Thin signal notification for encryption operations received from SignalR.
/// Contains only operation ID, progress percentage, and status — UI pulls
/// detailed per-recipient information through authenticated REST endpoints.
/// </summary>
public record EncryptionSignal
{
    public string OperationId { get; init; } = string.Empty;
    public int PercentComplete { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}
