// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.ServiceClients.Trust;

namespace Sorcha.Blueprint.Service.Credentials;

/// <summary>
/// Feature 181 US3 (T035) — Blueprint Service's service-layer adapter supplying the <c>trustlist</c> trust
/// source's X.509 anchors from an imported ETSI TS 119 612 snapshot, via the caching
/// <see cref="ITrustListProvider"/>.
/// </summary>
/// <remarks>
/// Deliberately nothing but wiring: fetch the snapshot with this service's configured client, then
/// hand its facts to <see cref="TrustListAnchorDecision"/>, which owns the fail-closed and freshness
/// rules (FR-014 / FR-015 / FR-016). Each service needs its own adapter because each resolves
/// snapshots through its own client, but the decision itself exists once — this file and HAIP's
/// counterpart were previously byte-identical apart from their comments, which is one copy of a
/// fail-closed rule too many (DRIFT-005).
/// </remarks>
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
        if (snapshot is null)
        {
            return null;
        }

        return TrustListAnchorDecision.Evaluate(
            snapshot.Roots,
            snapshot.Id,
            snapshot.SequenceNumber,
            snapshot.Freshness,
            TrustListAnchorFreshness.IsStale(snapshot, _clock.GetUtcNow()),
            _strictFreshness,
            _logger);
    }
}
