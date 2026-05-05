// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Hubs;

/// <summary>
/// Group-name builder for <see cref="WalletHub"/>. All WalletHub group strings
/// MUST be constructed via these helpers — no inline interpolation in service code.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-013 — FR-015. Formalises the pre-existing
/// <see cref="WalletHub.GroupNameFor"/> helper used by Feature 114 citizen wallet.
/// The CI grep gate (Phase 7 / US5) enforces this retroactively.
/// </remarks>
public static class WalletHubGroups
{
    /// <summary>Citizen-wallet group: per-PlatformUser, used by Feature 114 device events.</summary>
    public static string CitizenWallet(Guid platformUserId) => WalletHub.GroupNameFor(platformUserId);

    /// <summary>Per-wallet-address group. Hosts encryption progress, credential events, transaction-tick state.</summary>
    public static string Wallet(string walletAddress) => $"wallet:{walletAddress}";
}
