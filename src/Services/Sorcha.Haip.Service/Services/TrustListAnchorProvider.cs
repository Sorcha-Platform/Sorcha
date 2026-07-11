// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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

    public TrustListAnchorProvider(ITrustListProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

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

        return new TrustAnchorSet
        {
            Roots = snapshot.Roots,
            AnchorSetId = $"{snapshot.Id}#{snapshot.SequenceNumber}",
            Freshness = snapshot.Freshness,
            CheckRevocation = false,
        };
    }
}
