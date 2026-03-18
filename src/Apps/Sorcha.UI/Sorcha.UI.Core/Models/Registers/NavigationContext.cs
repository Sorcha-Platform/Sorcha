// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// Represents the current navigation depth within the transaction explorer.
/// </summary>
public enum NavigationLevel
{
    /// <summary>
    /// Viewing the register overview
    /// </summary>
    Register,

    /// <summary>
    /// Viewing a specific docket
    /// </summary>
    Docket,

    /// <summary>
    /// Viewing a specific transaction
    /// </summary>
    Transaction
}

/// <summary>
/// Tracks the current navigation state for breadcrumb rendering in the transaction explorer.
/// </summary>
public record ExplorerNavigationContext
{
    /// <summary>
    /// Current navigation depth level
    /// </summary>
    public NavigationLevel Level { get; init; } = NavigationLevel.Register;

    /// <summary>
    /// The selected docket identifier, if navigated into a docket
    /// </summary>
    public string? DocketId { get; init; }

    /// <summary>
    /// The selected docket version number, if navigated into a docket
    /// </summary>
    public ulong? DocketVersion { get; init; }

    /// <summary>
    /// The selected transaction identifier, if navigated into a transaction
    /// </summary>
    public string? TransactionId { get; init; }

    /// <summary>
    /// Truncated transaction ID for display purposes
    /// </summary>
    public string TransactionIdTruncated => TransactionId is { Length: > 8 }
        ? $"{TransactionId[..8]}..."
        : TransactionId ?? "";
}
