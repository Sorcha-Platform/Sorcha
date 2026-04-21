// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.Register.Models.Observations;

namespace Sorcha.Register.Core.SyncState;

/// <summary>
/// Default implementation of <see cref="IRegisterSyncStateResolver"/>.
/// State derivation rules (Feature 108):
/// <list type="bullet">
///   <item>No observations within staleness window → <c>Indeterminate</c>.</item>
///   <item>Local height strictly less than high-water-mark → <c>Syncing</c> (gap = HWM − local).</item>
///   <item>Local height == HWM and ≥quorum distinct peers agree (or single-peer-mode fallback) → <c>CaughtUp</c>.</item>
///   <item>Persisted <c>Error</c> overrides other signals until caller clears it.</item>
/// </list>
/// </summary>
public sealed class RegisterSyncStateResolver : IRegisterSyncStateResolver
{
    private readonly RegisterSyncStateOptions _options;

    public RegisterSyncStateResolver(IOptions<RegisterSyncStateOptions> options)
    {
        _options = options?.Value ?? new RegisterSyncStateOptions();
    }

    /// <inheritdoc />
    public TimeSpan StalenessWindow => _options.StalenessWindow;

    /// <inheritdoc />
    public RegisterSyncStateView Resolve(
        string registerId,
        uint localHeight,
        IReadOnlyList<PeerHeightObservation> observations,
        ValidatorSealingObservation? validatorSealing,
        RegisterSyncState? persistedState,
        string? lastErrorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(observations);

        // Error is sticky until caller clears it.
        if (persistedState == RegisterSyncState.Error)
        {
            return BuildView(
                registerId, RegisterSyncState.Error, localHeight,
                networkHwm: NullIfZero(MaxHeight(observations)),
                distinctPeers: observations.Count,
                lastAdvertAt: observations.Count == 0 ? null : observations[0].ObservedAt,
                singlePeerMode: observations.Count == 1,
                lastErrorMessage: lastErrorMessage,
                validatorSealing);
        }

        if (observations.Count == 0)
        {
            return BuildView(
                registerId, RegisterSyncState.Indeterminate, localHeight,
                networkHwm: null, distinctPeers: 0, lastAdvertAt: null,
                singlePeerMode: false, lastErrorMessage: null, validatorSealing);
        }

        var hwm = MaxHeight(observations);
        var singlePeerMode = observations.Count < _options.CaughtUpQuorum;

        RegisterSyncState state;
        if ((long)localHeight < hwm)
        {
            state = RegisterSyncState.Syncing;
        }
        else
        {
            // local >= hwm. CaughtUp regardless of single-peer mode (flag exposed).
            state = RegisterSyncState.CaughtUp;
        }

        return BuildView(
            registerId, state, localHeight,
            networkHwm: hwm,
            distinctPeers: observations.Count,
            lastAdvertAt: observations[0].ObservedAt,
            singlePeerMode: singlePeerMode,
            lastErrorMessage: null,
            validatorSealing);
    }

    private static long MaxHeight(IReadOnlyList<PeerHeightObservation> observations)
    {
        var max = 0L;
        for (var i = 0; i < observations.Count; i++)
        {
            if (observations[i].NetworkHeight > max)
                max = observations[i].NetworkHeight;
        }
        return max;
    }

    private static long? NullIfZero(long value) => value == 0 ? null : value;

    private static RegisterSyncStateView BuildView(
        string registerId,
        RegisterSyncState state,
        uint localHeight,
        long? networkHwm,
        int distinctPeers,
        DateTimeOffset? lastAdvertAt,
        bool singlePeerMode,
        string? lastErrorMessage,
        ValidatorSealingObservation? validatorSealing)
    {
        ValidatorSealingSnapshot? snapshot = validatorSealing is null
            ? null
            : new ValidatorSealingSnapshot(
                validatorSealing.LastSealedHeight,
                validatorSealing.MempoolDepth,
                validatorSealing.ObservedAt);

        return new RegisterSyncStateView(
            RegisterId: registerId,
            State: state,
            LocalHeight: localHeight,
            NetworkHeightHighWaterMark: networkHwm,
            DistinctPeerObservers: distinctPeers,
            LastAdvertAt: lastAdvertAt,
            SinglePeerMode: singlePeerMode,
            LastErrorMessage: lastErrorMessage,
            ValidatorSnapshot: snapshot);
    }
}
