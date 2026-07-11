// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

namespace Sorcha.ServiceClients.Trust;

/// <summary>
/// An operator-curated trust-list snapshot (feature 135, US2): a frozen set of trusted X.509 roots
/// with provenance and freshness. The clarified design (A5) uses operator-pushed snapshots; a live
/// LOTL feed is deferred.
/// </summary>
public sealed class TrustListSnapshot
{
    /// <summary>Stable snapshot identifier (referenced by a policy's <c>trustListId</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Trusted root certificates, DER-encoded.</summary>
    public IReadOnlyList<byte[]> Roots { get; init; } = [];

    /// <summary>Where the snapshot came from (e.g. an LOTL URL or "operator-upload").</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>When the snapshot was created/ingested.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The freshness timestamp recorded in trust evidence.</summary>
    public DateTimeOffset Freshness { get; init; }

    /// <summary>Feature 181 US3 — the list's own sequence number; forms the anchor-set identity
    /// <c>{trustListId}#{sequenceNumber}</c> carried into trust evidence (FR-015).</summary>
    public long SequenceNumber { get; init; }

    /// <summary>Feature 181 US3 — the list's declared next-update; null when the list carried none.</summary>
    public DateTimeOffset? NextUpdate { get; init; }

    /// <summary>Feature 181 US3 — computed freshness state (<c>Fresh</c> / <c>Stale</c>) at read time (FR-016).</summary>
    public string? FreshnessState { get; init; }
}

/// <summary>Feature 181 US3 — freshness evaluation over a wire <see cref="TrustListSnapshot"/> (FR-016).</summary>
public static class TrustListAnchorFreshness
{
    /// <summary>
    /// A snapshot is stale once <paramref name="now"/> reaches its declared <c>NextUpdate</c>; when the
    /// list carried no next-update, the server-computed <c>FreshnessState</c> is authoritative.
    /// </summary>
    public static bool IsStale(TrustListSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.NextUpdate is { } nextUpdate
            ? now >= nextUpdate
            : string.Equals(snapshot.FreshnessState, "Stale", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Resolves trust-list snapshots by id (feature 135). The trust-list trust source loads the returned
/// roots into the X.509 chain's custom trust store.
/// </summary>
public interface ITrustListProvider
{
    /// <summary>Returns the snapshot for <paramref name="trustListId"/>, or null when unknown.</summary>
    Task<TrustListSnapshot?> GetSnapshotAsync(string trustListId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory operator-snapshot trust-list provider (feature 135, US2). Holds snapshots pushed by the
/// trust-list admin surface; a live LOTL fetch is deferred (clarification A5). Registered as a
/// singleton so admin upserts and verification reads share state within a service.
/// </summary>
public sealed class OperatorSnapshotTrustListProvider : ITrustListProvider
{
    private readonly ConcurrentDictionary<string, TrustListSnapshot> _snapshots = new(StringComparer.Ordinal);

    /// <summary>Adds or replaces a snapshot.</summary>
    public void Upsert(TrustListSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshots[snapshot.Id] = snapshot;
    }

    /// <summary>Removes a snapshot; returns whether it existed.</summary>
    public bool Remove(string trustListId) => _snapshots.TryRemove(trustListId, out _);

    /// <summary>All known snapshots.</summary>
    public IReadOnlyCollection<TrustListSnapshot> List() => _snapshots.Values.ToArray();

    /// <inheritdoc />
    public Task<TrustListSnapshot?> GetSnapshotAsync(string trustListId, CancellationToken cancellationToken = default)
        => Task.FromResult(_snapshots.TryGetValue(trustListId, out var snapshot) ? snapshot : null);
}
