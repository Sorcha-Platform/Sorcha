// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>GET /api/v1/wallet/exists</c> (Feature 149). Lets the
/// Citizen Wallet PWA's pairing takeover distinguish "no wallet yet" (route the
/// citizen to web wallet creation) from "wallet exists, no device here" (offer
/// the pair flow). Carries a boolean only — never a wallet address or other PII.
/// </summary>
public sealed record WalletExistsResponse
{
    /// <summary>
    /// <c>true</c> when a wallet resolves for the calling citizen, else
    /// <c>false</c>.
    /// </summary>
    public bool HasWallet { get; init; }
}
