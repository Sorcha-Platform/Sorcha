// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for a page of wallet addresses.
/// </summary>
public sealed record AddressListResponse
{
    /// <summary>Parent wallet address.</summary>
    public required string WalletAddress { get; init; }

    /// <summary>List of addresses.</summary>
    public List<WalletAddressDto> Addresses { get; init; } = new();

    /// <summary>Total count (for pagination).</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Whether more pages remain after the current one.</summary>
    public bool HasMore => (Page * PageSize) < TotalCount;
}
