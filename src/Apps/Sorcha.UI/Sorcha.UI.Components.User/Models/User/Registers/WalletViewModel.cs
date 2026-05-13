// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// Wallet information for selection dropdown in register creation wizard.
/// </summary>
public record WalletViewModel
{
    /// <summary>
    /// Wallet address (public identifier, used as ID).
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// User-friendly wallet name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Cryptographic algorithm (ED25519, P-256, etc.).
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Wallet status (Active, Archived, etc.).
    /// </summary>
    public string Status { get; init; } = "Active";

    /// <summary>
    /// Whether this wallet can be used for signing.
    /// </summary>
    public bool CanSign => Status == "Active";

    /// <summary>
    /// Truncated address for display (first 8...last 4).
    /// </summary>
    public string AddressTruncated =>
        Address.Length > 16
            ? $"{Address[..8]}...{Address[^4..]}"
            : Address;

    /// <summary>
    /// Display text for dropdown.
    /// </summary>
    public string DisplayText => $"{Name} ({AddressTruncated})";
}
