// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.ServiceClients.Trust;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 181 US3 (T035) — HAIP's service-layer adapter supplying the <c>trustlist</c> trust source's
/// X.509 anchors from an imported ETSI TS 119 612 snapshot (via the caching
/// <see cref="ITrustListProvider"/>). Sets <see cref="TrustAnchorSet.AnchorSetId"/> to
/// <c>{trustListId}#{sequenceNumber}</c> so the snapshot identity flows into the trust evidence
/// (FR-015); returns null when the snapshot is absent so the source fails closed (FR-014).
/// </summary>
public sealed class TrustListAnchorProvider : ITenantTrustAnchorProvider
{
    private readonly ITrustListProvider _provider;
    private readonly bool _strictFreshness;
    private readonly TimeProvider _clock;
    private readonly ILogger<TrustListAnchorProvider>? _logger;

    public TrustListAnchorProvider(
        ITrustListProvider provider,
        IConfiguration? configuration = null,
        ILogger<TrustListAnchorProvider>? logger = null,
        TimeProvider? clock = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _strictFreshness = configuration?.GetValue<bool>("Trust:TrustListStrictFreshness") ?? false;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(anchorId))
        {
            return null;
        }

        var snapshot = await _provider.GetSnapshotAsync(anchorId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Roots.Count == 0)
        {
            return null;
        }

        // Feature 181 US3 (T036 / FR-016) — warn (default) flags + vouches; strict fails closed on stale.
        if (TrustListAnchorFreshness.IsStale(snapshot, _clock.GetUtcNow()))
        {
            TrustMetrics.RecordStaleEvaluation(snapshot.Id, snapshot.SequenceNumber, _strictFreshness);
            if (_strictFreshness)
            {
                _logger?.LogWarning(
                    "Trusted-list snapshot {TrustListId}#{Sequence} is stale and strict freshness is enabled — failing closed (TRUSTLIST_STALE).",
                    snapshot.Id, snapshot.SequenceNumber);
                return null;
            }

            _logger?.LogWarning(
                "Trusted-list snapshot {TrustListId}#{Sequence} is stale (warn mode) — vouching with a stale-flagged evidence trail.",
                snapshot.Id, snapshot.SequenceNumber);
        }

        return new TrustAnchorSet
        {
            Roots = snapshot.Roots,
            AnchorSetId = $"{snapshot.Id}#{snapshot.SequenceNumber}",
            Freshness = snapshot.Freshness,
            CheckRevocation = false,
        };
    }
}
