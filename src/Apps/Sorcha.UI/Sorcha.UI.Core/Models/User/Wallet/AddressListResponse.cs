// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Response model for listing wallet addresses
/// </summary>
public class AddressListResponse
{
    /// <summary>
    /// Parent wallet address
    /// </summary>
    public required string WalletAddress { get; set; }

    /// <summary>
    /// List of addresses
    /// </summary>
    public List<WalletAddressDto> Addresses { get; set; } = new();

    /// <summary>
    /// Total count (for pagination)
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Current page number
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// Has more pages
    /// </summary>
    public bool HasMore => (Page * PageSize) < TotalCount;
}
