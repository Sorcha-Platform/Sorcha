// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Composes and signs a citizen wallet device delegation credential
/// (Feature 114 / SD-JWT VC of type
/// <see cref="Sorcha.CitizenWallet.Abstractions.Constants.VctUris.CitizenDeviceDelegationV1"/>).
/// </summary>
/// <remarks>
/// The delegation credential proves that a specific device public key has been
/// authorised by the citizen's holder key to make presentations on their behalf.
/// Verifiers consume it offline as the second link in the issuer→holder→device
/// trust chain (the credential itself binds to the holder, the delegation binds
/// the holder to the device, the KB-JWT proves the device controls the bound key).
/// Issued under <see cref="IHolderKeyService"/> (slot 108) and pointed at a status
/// list maintained by <see cref="ICitizenStatusListPublisher"/> (slot 109).
/// </remarks>
public interface IDeviceDelegationIssuer
{
    /// <summary>
    /// Issues a fresh device delegation credential for the supplied device key.
    /// Allocates a status list index, composes the SD-JWT VC payload, and signs
    /// with the citizen's holder key.
    /// </summary>
    /// <param name="platformUserId">The owning citizen's PlatformUser id (audit / log scope only).</param>
    /// <param name="citizenWalletAddress">The citizen's primary Sorcha wallet — source for the slot-108 holder key.</param>
    /// <param name="organizationId">The org whose citizen-devices status list will track this delegation's revocation bit.</param>
    /// <param name="orgStatusSigningWalletAddress">The org's signing wallet — source for the slot-109 status-list signing key.</param>
    /// <param name="devicePublicJwk">The device's public JWK (already validated by FluentValidation).</param>
    /// <param name="deviceLabel">Citizen-visible device label.</param>
    /// <param name="platform">Free-form platform descriptor (e.g. "iOS 19 / Safari 19").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The issued delegation result.</returns>
    Task<DeviceDelegationResult> IssueAsync(
        Guid platformUserId,
        string citizenWalletAddress,
        Guid organizationId,
        string orgStatusSigningWalletAddress,
        EcP256PublicJwk devicePublicJwk,
        string deviceLabel,
        string platform,
        CancellationToken ct = default);
}

/// <summary>Outcome of a successful <see cref="IDeviceDelegationIssuer.IssueAsync"/> call.</summary>
/// <param name="CompactJwt">Compact-serialised SD-JWT VC ready to ship to the wallet.</param>
/// <param name="Jti">Stable identifier for the credential (rotates on renewal). Mirrored in <c>PlatformUserDevice.DelegationCredentialJti</c>.</param>
/// <param name="ExpiresAt">When the delegation credential expires (default issuance: now + 12 months).</param>
/// <param name="StatusListUri">Public URI of the status list that tracks this delegation.</param>
/// <param name="StatusListIndex">Bit position within that status list.</param>
/// <param name="StatusListId">Numeric identifier of the status list (lists roll over at <c>Capacity</c>).</param>
/// <param name="HolderPublicJwk">The citizen's holder public JWK as JSON — convenience copy for the wallet.</param>
public sealed record DeviceDelegationResult(
    string CompactJwt,
    string Jti,
    DateTimeOffset ExpiresAt,
    string StatusListUri,
    int StatusListIndex,
    int StatusListId,
    System.Text.Json.JsonElement HolderPublicJwk);
