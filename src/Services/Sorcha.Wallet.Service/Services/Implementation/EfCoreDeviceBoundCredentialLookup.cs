// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Sorcha.Cryptography;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// EF Core-backed <see cref="IDeviceBoundCredentialLookup"/>. Projects the live
/// device-bound copies of a credential type held by a citizen from the wallet
/// credential store (Feature 1195, Phase 2, Task 5).
/// </summary>
/// <remarks>
/// A credential is a <em>device-bound copy</em> when its <c>cnf</c> key is NOT the
/// citizen's holder key — the holder-bound web root shares the same <c>(user, vct)</c>
/// but its <c>cnf</c> thumbprint equals the holder-key thumbprint, so it is excluded
/// here and never counts against the device cap. Only <see cref="CredentialStatus.Active"/>
/// (presentable) copies are returned.
/// </remarks>
public sealed class EfCoreDeviceBoundCredentialLookup : IDeviceBoundCredentialLookup
{
    private readonly WalletDbContext _db;
    private readonly ICredentialStore _store;
    private readonly IHolderKeyService _holderKeyService;
    private readonly ILogger<EfCoreDeviceBoundCredentialLookup> _logger;

    /// <summary>Initialises a new <see cref="EfCoreDeviceBoundCredentialLookup"/>.</summary>
    public EfCoreDeviceBoundCredentialLookup(
        WalletDbContext db,
        ICredentialStore store,
        IHolderKeyService holderKeyService,
        ILogger<EfCoreDeviceBoundCredentialLookup> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _holderKeyService = holderKeyService ?? throw new ArgumentNullException(nameof(holderKeyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceBoundCredentialCopy>> GetLiveCopiesAsync(
        Guid userId, string credentialType, CancellationToken ct = default)
    {
        // Reverse the F114 holder index (userId → holder wallet address(es)). A citizen
        // normally holds one holder address; the loop tolerates re-enrolment rows.
        var walletAddresses = await _db.CitizenHolderIndex
            .AsNoTracking()
            .Where(e => e.PlatformUserId == userId)
            .Select(e => e.WalletAddress)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (walletAddresses.Count == 0)
        {
            return Array.Empty<DeviceBoundCredentialCopy>();
        }

        var copies = new List<DeviceBoundCredentialCopy>();

        foreach (var walletAddress in walletAddresses)
        {
            // The holder-key thumbprint distinguishes the web root (cnf == holder key)
            // from a device copy (cnf == device key). Resolve once per wallet.
            string holderThumbprint;
            try
            {
                holderThumbprint = await _holderKeyService
                    .GetHolderJwkThumbprintAsync(walletAddress, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Without the holder thumbprint we cannot exclude the root, so we would
                // risk counting it against the cap. Skip this wallet rather than guess.
                _logger.LogWarning(ex,
                    "Device-bound lookup: could not resolve holder thumbprint for wallet {Address}; skipping",
                    walletAddress);
                continue;
            }

            var stored = await _store.GetByWalletAsync(walletAddress, ct).ConfigureAwait(false);

            foreach (var credential in stored)
            {
                if (credential.Status != CredentialStatus.Active)
                {
                    continue; // revoked/expired/declined copies must not count against the cap
                }

                if (!string.Equals(credential.Type, credentialType, StringComparison.Ordinal))
                {
                    continue; // the cap is per credential type (vct)
                }

                var thumbprint = TryComputeCnfThumbprint(credential.RawToken);
                if (thumbprint is null)
                {
                    continue; // no cnf → not a bound copy
                }

                if (string.Equals(thumbprint, holderThumbprint, StringComparison.Ordinal))
                {
                    continue; // this is the holder-bound web root, not a device copy
                }

                copies.Add(new DeviceBoundCredentialCopy(
                    CredentialId: credential.Id,
                    DeviceKeyThumbprint: thumbprint,
                    IssuedAt: credential.IssuedAt,
                    // The stored credential carries no device linkage — device id/label are
                    // resolved cross-service (Tenant registry) which is out of scope here.
                    // The F118 eviction notice degrades to a no-op (non-fatal) without them.
                    DeviceId: Guid.Empty,
                    DeviceLabel: null));
            }
        }

        return copies;
    }

    /// <summary>
    /// Extracts the RFC 7638 thumbprint of the credential's non-disclosable <c>cnf</c>
    /// key from the SD-JWT body, or <c>null</c> if the token has no <c>cnf.jwk</c>.
    /// </summary>
    private static string? TryComputeCnfThumbprint(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        try
        {
            // cnf lives in the issuer-signed JWT body (before the first '~'), never in a
            // disclosure — it is always non-disclosable.
            var jwt = rawToken.Split('~')[0];
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var bodyBytes = Base64Url.DecodeFromChars(parts[1]);
            using var doc = JsonDocument.Parse(bodyBytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("cnf", out var cnf)
                || cnf.ValueKind != JsonValueKind.Object
                || !cnf.TryGetProperty("jwk", out var jwk)
                || jwk.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return JsonWebKeyThumbprint.Compute(jwk);
        }
        catch
        {
            // Malformed token / unsupported key — not a countable device copy.
            return null;
        }
    }
}
