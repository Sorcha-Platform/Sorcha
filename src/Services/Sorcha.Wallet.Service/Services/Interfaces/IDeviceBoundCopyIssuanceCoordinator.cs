// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// The Token Status List slot a device-bound credential copy must embed so it is
/// revocable by the citizen status-list publisher (Feature 114). Returned by
/// <see cref="IDeviceBoundCopyIssuanceCoordinator.PrepareAsync"/> when the mint is a
/// device-bound copy; the issuance path embeds it as the IETF <c>status.status_list</c>
/// claim and records it on the stored <c>CredentialEntity</c>.
/// </summary>
/// <param name="StatusListUrl">The F114 citizen-device status-list URI (<c>statuslist+jwt</c>).</param>
/// <param name="StatusListIndex">The allocated bit index within that list.</param>
public sealed record DeviceBoundMintPlan(string StatusListUrl, int StatusListIndex);

/// <summary>
/// Runs the device-bound credential copy policy (Feature 1195, Phase 2) at the mint
/// entrypoint. Detects whether an incoming issuance is a <em>device-bound copy</em>
/// (vs the holder-bound web root), enforces the max-3 cap with LRU eviction via
/// <see cref="IDeviceBoundCredentialPolicy"/>, and allocates a wallet-owned
/// (F114) status-list slot so the copy can later be revoked.
/// </summary>
/// <remarks>
/// Encapsulates the mint-path wiring so the <c>IssueCredential</c> endpoint gains a
/// single seam rather than five discrete dependencies, and so the discriminator +
/// policy + allocation are unit-testable in isolation.
/// <para>
/// <b>Discriminator.</b> A device-bound copy is one whose <c>cnf</c> key is NOT the
/// recipient's citizen holder key (slot 108). The web root is bound to the holder key
/// itself, so its <c>cnf</c> thumbprint equals the recipient's holder-key thumbprint;
/// a device copy is bound to the phone's device key, a different thumbprint. Comparing
/// thumbprints (rather than <c>cnf</c> curve) is robust even for P-256 wallets, whose
/// holder key is also P-256.
/// </para>
/// </remarks>
public interface IDeviceBoundCopyIssuanceCoordinator
{
    /// <summary>
    /// Called BEFORE signing a credential. Returns <c>null</c> when the mint is not a
    /// device-bound copy (the holder-bound web root, a non-citizen recipient, or an
    /// unbound credential) — the caller mints unchanged. Otherwise runs the eviction
    /// policy and returns the status-list slot the copy must embed.
    /// </summary>
    /// <param name="recipientWalletAddress">The credential recipient's wallet address.</param>
    /// <param name="credentialVct">The resolved credential type / <c>vct</c> (the cap key).</param>
    /// <param name="holderJwk">The incoming <c>cnf</c> key (device key for a device copy).</param>
    /// <param name="issuerOrgId">The issuing organisation id (owns the F114 status list).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status-list slot for a device-bound copy, or <c>null</c> to mint unchanged.</returns>
    /// <exception cref="Exception">
    /// Propagated from <see cref="IDeviceBoundCredentialPolicy.ReconcileAsync"/> when
    /// eviction (revoke-oldest) fails — the caller MUST abort issuance.
    /// </exception>
    Task<DeviceBoundMintPlan?> PrepareAsync(
        string recipientWalletAddress,
        string credentialVct,
        JsonElement holderJwk,
        Guid issuerOrgId,
        CancellationToken ct = default);
}
