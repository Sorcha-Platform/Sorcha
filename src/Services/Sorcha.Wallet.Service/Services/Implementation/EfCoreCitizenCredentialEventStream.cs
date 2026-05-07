// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// EF Core-backed implementation of <see cref="ICitizenCredentialEventStream"/>
/// (Feature 114 US4). Reads the citizen-scoped event log written by
/// <see cref="CitizenInboxProjector"/> joined to <see cref="CredentialEntity"/>
/// for payload composition.
/// </summary>
/// <remarks>
/// Replaces <c>EmptyCitizenCredentialEventStream</c> as the registered
/// <see cref="ICitizenCredentialEventStream"/>. The composition point in
/// <see cref="ICitizenSyncService"/> is unchanged; only the event source
/// switches from "always empty" to "real events from CredentialStore via the
/// projector."
/// </remarks>
public sealed class EfCoreCitizenCredentialEventStream : ICitizenCredentialEventStream
{
    private readonly WalletDbContext _db;
    private readonly ILogger<EfCoreCitizenCredentialEventStream> _logger;

    /// <summary>Initialises a new instance.</summary>
    public EfCoreCitizenCredentialEventStream(
        WalletDbContext db,
        ILogger<EfCoreCitizenCredentialEventStream> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CitizenCredentialEvent>> ReadAsync(
        Guid platformUserId,
        long afterSeq,
        CancellationToken ct = default)
    {
        var rows = await _db.CitizenCredentialEventLog
            .AsNoTracking()
            .Where(e => e.PlatformUserId == platformUserId && e.Seq > afterSeq)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return Array.Empty<CitizenCredentialEvent>();
        }

        // Resolve credential payloads in a single round-trip. The composite PK
        // on Credentials is (Id, WalletAddress); we don't have the WalletAddress
        // here but the recipient row is the one with status PendingAcceptance /
        // Active / Revoked / Declined and a SubjectDid that is the citizen's
        // holder address. For US4 v1 we match on credential id alone and prefer
        // the recipient row (i.e. NOT Active by issuer) — the issuer audit row
        // is harmless to surface but never the canonical citizen view.
        var credentialIds = rows.Select(r => r.CredentialId).Distinct().ToList();
        var credentials = await _db.Credentials
            .AsNoTracking()
            .Where(c => credentialIds.Contains(c.Id))
            .ToListAsync(ct);

        var byId = credentials
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, ChooseRecipientRow);

        var events = new List<CitizenCredentialEvent>(rows.Count);
        foreach (var row in rows)
        {
            if (!byId.TryGetValue(row.CredentialId, out var entity))
            {
                _logger.LogWarning(
                    "CitizenCredentialEventLog row {Seq} references missing credential {CredentialId} for platformUser {PlatformUserId} — skipping",
                    row.Seq, row.CredentialId, platformUserId);
                continue;
            }

            var kind = (CitizenCredentialEventKind)row.Kind;
            var payload = BuildPayload(kind, entity);
            events.Add(new CitizenCredentialEvent(row.Seq, kind, payload));
        }

        return events;
    }

    /// <inheritdoc />
    public async Task<long> GetHighestSeqAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var max = await _db.CitizenCredentialEventLog
            .AsNoTracking()
            .Where(e => e.PlatformUserId == platformUserId)
            .Select(e => (long?)e.Seq)
            .MaxAsync(ct);

        return max ?? 0L;
    }

    /// <summary>
    /// Prefer the credential row that represents the recipient's holding —
    /// <c>PendingAcceptance</c> / <c>Active</c> / <c>Declined</c> / <c>Revoked</c>
    /// over a co-located issuer audit row that is also <c>Active</c>. Falls
    /// back to the first match when no clearer signal is available.
    /// </summary>
    private static CredentialEntity ChooseRecipientRow(IGrouping<string, CredentialEntity> group)
    {
        return group.FirstOrDefault(c => c.Status is CredentialStatus.PendingAcceptance
                                                  or CredentialStatus.Declined
                                                  or CredentialStatus.Revoked)
            ?? group.First();
    }

    private static object BuildPayload(CitizenCredentialEventKind kind, CredentialEntity entity)
    {
        return kind switch
        {
            CitizenCredentialEventKind.Added => new CachedCredentialPayload
            {
                Id = entity.Id,
                Vct = entity.Type,
                Jwt = entity.RawToken,
                IssuerDid = entity.IssuerDid,
                IssuedAt = entity.IssuedAt,
                ExpiresAt = entity.ExpiresAt,
                StatusListUri = null,
                StatusListIndex = null,
            },
            CitizenCredentialEventKind.Revoked => new RevokedCredentialEntry
            {
                Id = entity.Id,
                Reason = MapRevocationReason(entity.Status),
                RevokedAt = DateTimeOffset.UtcNow,
            },
            CitizenCredentialEventKind.Replaced => new ReplacedCredentialEntry
            {
                OldId = entity.Id,
                NewId = entity.Id,
                Jwt = entity.RawToken,
                IssuedAt = entity.IssuedAt,
            },
            _ => new RevokedCredentialEntry
            {
                Id = entity.Id,
                Reason = CredentialRevocationReason.Erroneous,
                RevokedAt = DateTimeOffset.UtcNow,
            },
        };
    }

    private static CredentialRevocationReason MapRevocationReason(CredentialStatus status) =>
        status switch
        {
            CredentialStatus.Revoked => CredentialRevocationReason.Withdrawn,
            CredentialStatus.Declined => CredentialRevocationReason.Withdrawn,
            CredentialStatus.Expired => CredentialRevocationReason.Expired,
            _ => CredentialRevocationReason.Erroneous,
        };
}
