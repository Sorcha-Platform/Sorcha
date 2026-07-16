// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Revokes an evicted (or replaced) device-bound credential copy by flipping its
/// IETF Token Status List bit via the Feature 114 citizen status-list publisher, then
/// marking the stored copy <see cref="CredentialStatus.Revoked"/> so it no longer counts
/// as a live copy (Feature 1195, Phase 2, Task 5).
/// </summary>
/// <remarks>
/// Credential-copy revocation is distinct from device-delegation revocation
/// (<see cref="DeviceRevocationService"/>): here the status slot is read from the copy's
/// own <c>StatusListUrl</c>/<c>StatusListIndex</c> (allocated at device-copy mint time),
/// not from a device registry row. A revoke failure throws so the caller aborts issuance —
/// never leaving more than the cap of live copies.
/// </remarks>
public sealed partial class DeviceBoundCredentialRevoker : IDeviceBoundCredentialRevoker
{
    private readonly WalletDbContext _db;
    private readonly ICredentialStore _store;
    private readonly ICitizenStatusListPublisher _statusList;
    private readonly IOrgStatusSigningWalletResolver _orgWalletResolver;
    private readonly ILogger<DeviceBoundCredentialRevoker> _logger;

    /// <summary>Initialises a new <see cref="DeviceBoundCredentialRevoker"/>.</summary>
    public DeviceBoundCredentialRevoker(
        WalletDbContext db,
        ICredentialStore store,
        ICitizenStatusListPublisher statusList,
        IOrgStatusSigningWalletResolver orgWalletResolver,
        ILogger<DeviceBoundCredentialRevoker> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _statusList = statusList ?? throw new ArgumentNullException(nameof(statusList));
        _orgWalletResolver = orgWalletResolver ?? throw new ArgumentNullException(nameof(orgWalletResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task RevokeAsync(Guid userId, DeviceBoundCredentialCopy copy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        // Locate the stored copy under the citizen's holder wallet(s) so we can read its
        // status-list allocation. Credential ids are not globally unique (issuer + holder
        // rows), so scope the read to the citizen's own wallet.
        var walletAddresses = await _db.CitizenHolderIndex
            .AsNoTracking()
            .Where(e => e.PlatformUserId == userId)
            .Select(e => e.WalletAddress)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        CredentialEntity? entity = null;
        string? entityWallet = null;
        foreach (var walletAddress in walletAddresses)
        {
            entity = await _store.GetByIdForWalletAsync(copy.CredentialId, walletAddress, ct).ConfigureAwait(false);
            if (entity is not null)
            {
                entityWallet = walletAddress;
                break;
            }
        }

        if (entity is null || entityWallet is null)
        {
            throw new InvalidOperationException(
                $"Cannot revoke device-bound copy {copy.CredentialId}: no stored copy found for user {userId}.");
        }

        // Prefer the IETF status claim embedded in the signed token: a register-delivered
        // citizen copy (SorchaLocalWallet) arrives via InboundCredentialDetector, which does
        // not populate the StatusListUrl/StatusListIndex columns — but the signed body always
        // carries status.status_list.{uri,idx}. Fall back to the columns for the issuer copy.
        var (statusListUrl, statusListIndex) = ResolveStatusAllocation(entity);

        if (string.IsNullOrWhiteSpace(statusListUrl) || !statusListIndex.HasValue)
        {
            throw new InvalidOperationException(
                $"Cannot revoke device-bound copy {copy.CredentialId}: it has no status-list allocation " +
                "(neither an embedded IETF status claim nor StatusListUrl/StatusListIndex). " +
                "A device copy must be minted with a revocable slot.");
        }

        if (!TryParseCitizenStatusListUri(statusListUrl, out var organizationId, out var listId))
        {
            throw new InvalidOperationException(
                $"Cannot revoke device-bound copy {copy.CredentialId}: status-list URL '{statusListUrl}' " +
                "is not a recognised citizen-device status list.");
        }

        // Flip the revoked bit FIRST (this is what external verifiers honour). A failure
        // propagates so issuance aborts.
        var signingWallet = await _orgWalletResolver.ResolveAsync(organizationId, ct).ConfigureAwait(false);
        await _statusList.FlipAsync(organizationId, listId, statusListIndex.Value, signingWallet, ct)
            .ConfigureAwait(false);

        // Mark the stored copy Revoked so the live-copy lookup excludes it immediately.
        await _store.UpdateStatusAsync(copy.CredentialId, entityWallet, CredentialStatus.Revoked, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Revoked device-bound credential copy {CredentialId} for user {UserId} " +
            "(org={OrgId} list={ListId}#{Index})",
            copy.CredentialId, userId, organizationId, listId, statusListIndex.Value);
    }

    /// <summary>
    /// Resolves the copy's status-list allocation, preferring the IETF <c>status.status_list</c>
    /// claim in the signed token (populated on every device copy) and falling back to the
    /// <c>StatusListUrl</c>/<c>StatusListIndex</c> columns (populated on the issuer's copy).
    /// </summary>
    private static (string? Url, int? Index) ResolveStatusAllocation(CredentialEntity entity)
    {
        if (TryReadIetfStatusClaim(entity.RawToken, out var url, out var idx))
        {
            return (url, idx);
        }

        return (entity.StatusListUrl, entity.StatusListIndex);
    }

    private static bool TryReadIetfStatusClaim(string? rawToken, out string? uri, out int? idx)
    {
        uri = null;
        idx = null;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        try
        {
            var jwt = rawToken.Split('~')[0];
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return false;
            }

            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("status", out var status)
                || !status.TryGetProperty("status_list", out var statusList)
                || !statusList.TryGetProperty("uri", out var uriEl)
                || !statusList.TryGetProperty("idx", out var idxEl)
                || uriEl.ValueKind != JsonValueKind.String
                || idxEl.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            uri = uriEl.GetString();
            idx = idxEl.GetInt32();
            return !string.IsNullOrWhiteSpace(uri);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses the org id and list id out of a citizen-device status-list URI of the form
    /// <c>.../status/{orgId:N}/citizen-devices/{listId}.statuslist+jwt</c>
    /// (see <c>CitizenStatusListPublisher.BuildStatusListUri</c>).
    /// </summary>
    private static bool TryParseCitizenStatusListUri(string url, out Guid organizationId, out int listId)
    {
        organizationId = Guid.Empty;
        listId = -1;

        var match = CitizenStatusListUriRegex().Match(url);
        if (!match.Success)
        {
            return false;
        }

        return Guid.TryParseExact(match.Groups["org"].Value, "N", out organizationId)
            && int.TryParse(match.Groups["list"].Value, out listId);
    }

    [GeneratedRegex(@"/status/(?<org>[0-9a-fA-F]{32})/citizen-devices/(?<list>\d+)\.statuslist\+jwt$")]
    private static partial Regex CitizenStatusListUriRegex();
}
