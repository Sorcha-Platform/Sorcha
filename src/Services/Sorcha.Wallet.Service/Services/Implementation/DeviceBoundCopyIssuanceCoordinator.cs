// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Sorcha.Cryptography;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default <see cref="IDeviceBoundCopyIssuanceCoordinator"/> (Feature 1195, Phase 2, Task 5).
/// Runs the device-bound copy discriminator, the max-3 eviction policy, and the F114
/// status-slot allocation at the mint entrypoint.
/// </summary>
public sealed class DeviceBoundCopyIssuanceCoordinator : IDeviceBoundCopyIssuanceCoordinator
{
    private readonly IHolderAddressLookup _holderAddressLookup;
    private readonly IHolderKeyService _holderKeyService;
    private readonly IDeviceBoundCredentialPolicy _policy;
    private readonly IDeviceBoundCredentialLookup _lookup;
    private readonly IDeviceBoundCredentialRevoker _revoker;
    private readonly ICitizenStatusListPublisher _statusList;
    private readonly IOrgStatusSigningWalletResolver _orgWalletResolver;
    private readonly ILogger<DeviceBoundCopyIssuanceCoordinator> _logger;

    /// <summary>Initialises a new <see cref="DeviceBoundCopyIssuanceCoordinator"/>.</summary>
    public DeviceBoundCopyIssuanceCoordinator(
        IHolderAddressLookup holderAddressLookup,
        IHolderKeyService holderKeyService,
        IDeviceBoundCredentialPolicy policy,
        IDeviceBoundCredentialLookup lookup,
        IDeviceBoundCredentialRevoker revoker,
        ICitizenStatusListPublisher statusList,
        IOrgStatusSigningWalletResolver orgWalletResolver,
        ILogger<DeviceBoundCopyIssuanceCoordinator> logger)
    {
        _holderAddressLookup = holderAddressLookup ?? throw new ArgumentNullException(nameof(holderAddressLookup));
        _holderKeyService = holderKeyService ?? throw new ArgumentNullException(nameof(holderKeyService));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _revoker = revoker ?? throw new ArgumentNullException(nameof(revoker));
        _statusList = statusList ?? throw new ArgumentNullException(nameof(statusList));
        _orgWalletResolver = orgWalletResolver ?? throw new ArgumentNullException(nameof(orgWalletResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DeviceBoundMintPlan?> PrepareAsync(
        string recipientWalletAddress,
        string credentialVct,
        JsonElement holderJwk,
        Guid issuerOrgId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientWalletAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialVct);

        // 1. The policy applies only to citizens (a resolvable PlatformUserId). A device
        //    copy delivered to a non-citizen recipient (e.g. an org wallet) is out of scope —
        //    mint proceeds unchanged.
        var platformUserId = await _holderAddressLookup
            .ResolvePlatformUserIdAsync(recipientWalletAddress, ct)
            .ConfigureAwait(false);
        if (platformUserId is null)
        {
            return null;
        }

        // 2. Discriminate the device copy from the holder-bound web root. The root's cnf IS
        //    the citizen's holder key, so its thumbprint equals the holder-key thumbprint; a
        //    device copy's cnf is the phone's device key, a different thumbprint. This holds
        //    even for P-256 wallets (whose holder key is also P-256), unlike a curve check.
        string deviceThumbprint;
        try
        {
            deviceThumbprint = JsonWebKeyThumbprint.Compute(holderJwk);
        }
        catch (ArgumentException ex)
        {
            // An unparseable cnf is not a device copy we can cap — mint unchanged.
            _logger.LogWarning(ex,
                "Device-bound coordinator: incoming cnf JWK is not a computable thumbprint; treating as non-device mint");
            return null;
        }

        // FAIL-CLOSED BY DESIGN: a holder-key resolution fault propagates and aborts the mint.
        // Degrading here would either misclassify a device copy as the web root (minting it
        // WITHOUT a revocable status slot — a permanently unrevocable artifact) or bypass the
        // device cap entirely. Transient faults are retryable by the caller (the endpoint maps
        // them to 503), and holder thumbprints are Redis-cached (24h) so faults are rare.
        var holderThumbprint = await _holderKeyService
            .GetHolderJwkThumbprintAsync(recipientWalletAddress, ct)
            .ConfigureAwait(false);

        if (string.Equals(deviceThumbprint, holderThumbprint, StringComparison.Ordinal))
        {
            // Holder-bound web root — NOT a device copy. Mint unchanged (no policy, no slot).
            return null;
        }

        // 3+4. It is a device-bound copy. Enforce the cap (may evict the oldest via the
        //      status-list + inbox); on ReplaceExisting (idempotent re-bind of the same device)
        //      revoke the prior same-thumbprint copy so the new one replaces it in place.
        //      Failures in this block are POLICY REFUSALS — the cap could not be honoured
        //      (revoke failed / reconcile refused) — surfaced as the typed exception so the
        //      endpoint maps them to 409 (vs 503 for infrastructure faults elsewhere).
        try
        {
            var disposition = await _policy
                .ReconcileAsync(platformUserId.Value, credentialVct, deviceThumbprint, ct)
                .ConfigureAwait(false);

            if (disposition.Kind == DeviceBindKind.ReplaceExisting)
            {
                await RevokePriorCopyForThumbprintAsync(platformUserId.Value, credentialVct, deviceThumbprint, ct)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Device-bound copy reconcile: user={UserId} vct={Vct} disposition={Disposition}",
                platformUserId.Value, credentialVct, disposition.Kind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DeviceBoundPolicyRefusalException(
                $"Device-bound copy policy refused the mint for user {platformUserId.Value}: " +
                "the cap could not be honoured (eviction/replacement revoke failed).", ex);
        }

        // 5. Allocate a wallet-owned (F114) status-list slot so the copy is revocable. Done
        //    after the policy so a reconcile abort does not leak an index.
        var signingWallet = await _orgWalletResolver.ResolveAsync(issuerOrgId, ct).ConfigureAwait(false);
        var (listId, index) = await _statusList
            .AllocateIndexAsync(issuerOrgId, signingWallet, ct)
            .ConfigureAwait(false);
        var statusListUrl = _statusList.BuildStatusListUri(issuerOrgId, listId);

        _logger.LogInformation(
            "Device-bound copy mint plan: user={UserId} vct={Vct} statusSlot={Org}/{ListId}#{Index}",
            platformUserId.Value, credentialVct, issuerOrgId, listId, index);

        return new DeviceBoundMintPlan(statusListUrl, index);
    }

    private async Task RevokePriorCopyForThumbprintAsync(
        Guid userId, string credentialVct, string deviceThumbprint, CancellationToken ct)
    {
        var liveCopies = await _lookup.GetLiveCopiesAsync(userId, credentialVct, ct).ConfigureAwait(false);
        var prior = liveCopies.FirstOrDefault(
            c => string.Equals(c.DeviceKeyThumbprint, deviceThumbprint, StringComparison.Ordinal));

        if (prior is null)
        {
            // Nothing live to replace (raced away / already revoked) — the new copy stands alone.
            return;
        }

        // A revoke failure propagates: aborting is safer than minting a second live copy
        // for the same device.
        await _revoker.RevokeAsync(userId, prior, ct).ConfigureAwait(false);
    }
}
