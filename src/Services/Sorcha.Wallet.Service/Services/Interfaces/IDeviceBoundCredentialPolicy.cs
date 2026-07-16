// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Outcome kind returned by <see cref="IDeviceBoundCredentialPolicy.ReconcileAsync"/>
/// describing how a device-bound credential copy request relates to the live copies
/// already held for the same <c>(user, credentialType)</c>.
/// </summary>
public enum DeviceBindKind
{
    /// <summary>The device is new and the live-copy count is still below the cap — mint freely.</summary>
    NewWithinCap,

    /// <summary>
    /// The device key thumbprint matches an existing live copy — the mint is an idempotent
    /// re-issue for the same device. No eviction; the live-copy count is unchanged.
    /// The policy does NOT revoke the prior same-thumbprint copy — replacing the stored
    /// copy is the caller's (the mint path's) responsibility.
    /// </summary>
    ReplaceExisting,

    /// <summary>
    /// The device is new and the cap is already reached — the oldest live copy has been
    /// evicted (status-list revoke + inbox notify) to make room. See
    /// <see cref="DeviceBindDisposition.EvictedCredentialId"/>.
    /// </summary>
    NewWithEviction
}

/// <summary>
/// Result of reconciling a device-bound credential copy request against the live copies.
/// </summary>
/// <param name="Kind">How the request relates to the existing live copies.</param>
/// <param name="EvictedCredentialId">
/// The credential id of the copy evicted to honour the cap, or <c>null</c> when no
/// eviction occurred (<see cref="DeviceBindKind.NewWithinCap"/> /
/// <see cref="DeviceBindKind.ReplaceExisting"/>).
/// </param>
public sealed record DeviceBindDisposition(DeviceBindKind Kind, string? EvictedCredentialId);

/// <summary>
/// A live device-bound credential copy as seen by the eviction policy. Projected from the
/// wallet credential store by an <see cref="IDeviceBoundCredentialLookup"/> implementation
/// (Task 5) so the policy stays a pure orchestration over data, free of SD-JWT parsing.
/// </summary>
/// <param name="CredentialId">The stored credential's id (returned as the evicted id).</param>
/// <param name="DeviceKeyThumbprint">RFC 7638 thumbprint of the copy's device <c>cnf</c> key.</param>
/// <param name="IssuedAt">When the copy was issued — the LRU ordering key (oldest = smallest).</param>
/// <param name="DeviceId">The bound device's id, used for the F118 inbox notification.</param>
/// <param name="DeviceLabel">Human-readable device label for the inbox entry, or <c>null</c>.</param>
public sealed record DeviceBoundCredentialCopy(
    string CredentialId,
    string DeviceKeyThumbprint,
    DateTimeOffset IssuedAt,
    Guid DeviceId,
    string? DeviceLabel);

/// <summary>
/// Loads the live (presentable) device-bound credential copies for a citizen and credential
/// type. Task 5 supplies the concrete implementation over the wallet credential store; the
/// policy consumes this seam so it can be unit-tested without a database or SD-JWT decoding.
/// </summary>
public interface IDeviceBoundCredentialLookup
{
    /// <summary>
    /// Returns every live device-bound copy of <paramref name="credentialType"/> currently held
    /// by <paramref name="userId"/>. Copies whose status is no longer presentable (revoked,
    /// expired, declined) MUST be excluded so they do not count against the device cap.
    /// </summary>
    Task<IReadOnlyList<DeviceBoundCredentialCopy>> GetLiveCopiesAsync(
        Guid userId, string credentialType, CancellationToken ct = default);
}

/// <summary>
/// Revokes a single evicted device-bound credential copy (flips its status-list bit).
/// Split out as its own seam because credential-level status-list revocation is distinct from
/// device-delegation revocation (Feature 114); Task 5 supplies the concrete revoker. Revocation
/// failure MUST propagate — the caller aborts issuance rather than leave partial state.
/// </summary>
public interface IDeviceBoundCredentialRevoker
{
    /// <summary>
    /// Revokes the supplied <paramref name="copy"/> for <paramref name="userId"/>. Throwing
    /// aborts the reconcile (no inbox write, no disposition returned).
    /// </summary>
    Task RevokeAsync(Guid userId, DeviceBoundCredentialCopy copy, CancellationToken ct = default);
}

/// <summary>
/// Issuer-side business policy enforcing at most <c>MAX_DEVICES</c> live device-bound copies of a
/// credential per citizen, keyed on the device key JWK thumbprint (RFC 7638), with LRU eviction
/// (Feature 1195, Phase 2).
/// </summary>
public interface IDeviceBoundCredentialPolicy
{
    /// <summary>
    /// Called BEFORE minting a device-bound copy. Enforces max-3 per <c>(user, credentialType)</c>
    /// keyed on device-key JWK thumbprint. Returns the disposition; performs eviction
    /// (status-list revoke + inbox notify) as a side-effect when a NEW device exceeds the cap.
    /// </summary>
    /// <param name="userId">The citizen (platform user) requesting the device-bound copy.</param>
    /// <param name="credentialType">The credential type being bound (the cap is per type).</param>
    /// <param name="deviceKeyThumbprint">RFC 7638 thumbprint of the requesting device's <c>cnf</c> key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The disposition. On revoke failure the task faults (issuance must abort).</returns>
    Task<DeviceBindDisposition> ReconcileAsync(
        Guid userId, string credentialType, string deviceKeyThumbprint, CancellationToken ct = default);
}
