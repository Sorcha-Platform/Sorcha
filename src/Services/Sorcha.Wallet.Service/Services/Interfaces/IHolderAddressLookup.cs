// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Resolves a citizen holder wallet address to its owning <c>PlatformUserId</c>
/// (Feature 114, US4).
/// </summary>
/// <remarks>
/// Backed by <c>CitizenHolderIndex</c>. Populated at device-enrolment time
/// (the moment <c>walletAddress</c> + <c>platformUserId</c> are first available
/// together via the citizen JWT) and consumed by
/// <see cref="ICitizenInboxProjector"/> when an inbound credential is detected
/// — at that point the projector only knows the recipient address, not the
/// citizen identity, so the index is the bridge.
/// </remarks>
public interface IHolderAddressLookup
{
    /// <summary>
    /// Returns the owning <c>PlatformUserId</c> for the given citizen holder
    /// wallet address, or <c>null</c> if the address is not a known citizen
    /// holder (e.g. an org wallet).
    /// </summary>
    Task<Guid?> ResolvePlatformUserIdAsync(string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Persists the <paramref name="walletAddress"/> → <paramref name="platformUserId"/>
    /// mapping. Idempotent — safe to call on every enrolment retry. Existing
    /// rows are left untouched.
    /// </summary>
    Task RegisterAsync(string walletAddress, Guid platformUserId, CancellationToken ct = default);
}
