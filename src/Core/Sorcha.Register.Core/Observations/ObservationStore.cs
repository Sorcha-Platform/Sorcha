// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models.Observations;

namespace Sorcha.Register.Core.Observations;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IObservationStore"/>.
/// </summary>
/// <remarks>
/// Per-register peer observations are stored in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// keyed by <c>SourcePeerId</c>. When the number of distinct peers for a register exceeds
/// <see cref="MaxDistinctPeersPerRegister"/>, the oldest entry (by <c>ObservedAt</c>) is evicted
/// on the next write to make room.
/// </remarks>
public sealed class ObservationStore : IObservationStore
{
    /// <summary>Cap per-register distinct peer observations to bound memory usage.</summary>
    public const int MaxDistinctPeersPerRegister = 16;

    private readonly ILogger<ObservationStore>? _logger;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PeerHeightObservation>> _peerObservations
        = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ValidatorSealingObservation> _validatorObservations
        = new(StringComparer.Ordinal);

    public ObservationStore() : this(null) { }

    public ObservationStore(ILogger<ObservationStore>? logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RecordPeerHeight(PeerHeightObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var perRegister = _peerObservations.GetOrAdd(
            observation.RegisterId,
            _ => new ConcurrentDictionary<string, PeerHeightObservation>(StringComparer.Ordinal));

        perRegister[observation.SourcePeerId] = observation;

        // Drain any excess — under concurrent writers, count can transiently exceed the cap.
        while (perRegister.Count > MaxDistinctPeersPerRegister)
        {
            if (!EvictOldestPeerObservation(observation.RegisterId, perRegister))
                break;
        }
    }

    /// <inheritdoc />
    public void RecordValidatorSealing(ValidatorSealingObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _validatorObservations[observation.RegisterId] = observation;
    }

    /// <inheritdoc />
    public IReadOnlyList<PeerHeightObservation> GetRecentPeerHeights(string registerId, TimeSpan stalenessWindow)
    {
        if (!_peerObservations.TryGetValue(registerId, out var perRegister))
            return Array.Empty<PeerHeightObservation>();

        var cutoff = DateTimeOffset.UtcNow - stalenessWindow;
        var snapshot = perRegister.Values
            .Where(o => o.ObservedAt >= cutoff)
            .OrderByDescending(o => o.ObservedAt)
            .ToArray();

        return snapshot;
    }

    /// <inheritdoc />
    public ValidatorSealingObservation? GetLatestValidatorSealing(string registerId)
    {
        _validatorObservations.TryGetValue(registerId, out var obs);
        return obs;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetTrackedRegisterIds()
    {
        var combined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in _peerObservations.Keys) combined.Add(id);
        foreach (var id in _validatorObservations.Keys) combined.Add(id);
        return combined;
    }

    /// <inheritdoc />
    public void Evict(string registerId)
    {
        _peerObservations.TryRemove(registerId, out _);
        _validatorObservations.TryRemove(registerId, out _);
    }

    private bool EvictOldestPeerObservation(
        string registerId,
        ConcurrentDictionary<string, PeerHeightObservation> perRegister)
    {
        // Single-pass min-scan. The previous OrderBy(...).FirstOrDefault() allocated
        // an array and an enumerator on every advert-ingest. At the 16-peer cap this
        // is negligible, but the method is called from the hot path so the cheaper
        // form is preferred.
        PeerHeightObservation? oldest = null;
        foreach (var obs in perRegister.Values)
        {
            if (oldest is null || obs.ObservedAt < oldest.ObservedAt)
                oldest = obs;
        }
        if (oldest is null) return false;

        if (perRegister.TryRemove(oldest.SourcePeerId, out _))
        {
            _logger?.LogDebug(
                "Evicted oldest peer observation for register {RegisterId} (peer {PeerId}, observed at {ObservedAt})",
                registerId, oldest.SourcePeerId, oldest.ObservedAt);
            return true;
        }

        return false;
    }
}
