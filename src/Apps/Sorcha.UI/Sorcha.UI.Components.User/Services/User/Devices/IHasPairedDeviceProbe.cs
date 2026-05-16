// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Devices;

/// <summary>
/// Aggregate "does this citizen have any paired device?" signal shared
/// between the wallet PWA (drives the F128 pairing takeover trigger) and
/// the Sorcha Web user-facing host (drives the nag-banner trigger). Calls
/// <c>GET /api/v1/me/devices/has-any</c> and caches the result for the
/// session.
/// </summary>
/// <remarks>
/// <para>
/// The probe is advisory for UX — not authoritative for auth. A transient
/// stale window (≤ hub-event delivery latency on remote pair-success) is
/// acceptable; the takeover dismisses on local pair-success via
/// <see cref="RaiseLocalPairCompleted"/> without waiting for the hub round-trip.
/// </para>
/// <para>
/// Sub-PR A2 ships the local-invalidation path only; the hub-event
/// subscription (which dismisses the takeover on remote pair-success) lands
/// in sub-PR A3 alongside the takeover itself.
/// </para>
/// </remarks>
public interface IHasPairedDeviceProbe
{
    /// <summary>
    /// Latest known value for the calling citizen. <c>null</c> means the
    /// probe has not yet completed an initial fetch — callers should treat
    /// this as "unknown, do not gate UX yet" and await
    /// <see cref="EnsureLoadedAsync"/> before rendering pairing-conditional
    /// surfaces.
    /// </summary>
    bool? HasAnyDevice { get; }

    /// <summary>
    /// Most recent active-device enrolment timestamp, or <c>null</c> when
    /// <see cref="HasAnyDevice"/> is <c>false</c> or unknown.
    /// </summary>
    DateTimeOffset? LatestEnrolledAt { get; }

    /// <summary>
    /// Fired whenever the cached value changes (initial fetch, refresh,
    /// local pair-success invalidation, or future hub-event invalidation).
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Idempotent — if the probe already has a value, returns immediately.
    /// Otherwise performs the initial HTTP fetch.
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken ct = default);

    /// <summary>
    /// Force a fresh HTTP fetch regardless of cache state. Used after the
    /// pair ceremony completes locally so the takeover dismisses without
    /// waiting on the next natural refresh cycle.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Signals that pairing just completed locally on this device. Triggers
    /// an immediate refresh + <see cref="Changed"/> notification. Surface
    /// this on every successful F114 pair-ceremony invocation so any open
    /// pairing UX dismisses promptly.
    /// </summary>
    Task RaiseLocalPairCompleted(CancellationToken ct = default);
}
