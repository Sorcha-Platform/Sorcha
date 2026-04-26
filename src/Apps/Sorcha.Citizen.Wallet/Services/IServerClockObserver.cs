// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Tracks the most recent server time the wallet has observed (Feature 114, T101).
/// HTTP responses from the Wallet Service carry a <c>Date</c> header; the
/// <see cref="ServerClockHandler"/> DelegatingHandler captures it on every
/// outbound call. Pages read <see cref="GetSkew"/> to surface a banner when
/// the device clock has drifted more than the documented threshold (per
/// FR-020: warn at 5 minutes, block presentations at 60 minutes).
/// </summary>
public interface IServerClockObserver
{
    /// <summary>Record an observation of the server's clock time.</summary>
    void Record(DateTimeOffset serverTime);

    /// <summary>
    /// Returns <c>(observed-at, server-time, deviceMinusServer)</c> from the
    /// most recent observation, or null if the wallet has never made an
    /// authenticated server call this session.
    /// </summary>
    ServerClockObservation? GetLatest();

    /// <summary>
    /// Convenience: returns the device-minus-server skew, or
    /// <see cref="TimeSpan.Zero"/> if no observation exists.
    /// </summary>
    TimeSpan GetSkew();
}

/// <summary>One observation of server time relative to device time.</summary>
/// <param name="ObservedAtDeviceUtc">Device's UTC time when the observation was recorded.</param>
/// <param name="ServerUtc">Server's UTC time as advertised in the HTTP <c>Date</c> header.</param>
/// <param name="Skew">Device minus server (positive = device clock ahead).</param>
public sealed record ServerClockObservation(
    DateTimeOffset ObservedAtDeviceUtc,
    DateTimeOffset ServerUtc,
    TimeSpan Skew);

/// <summary>Default thread-safe in-memory <see cref="IServerClockObserver"/>.</summary>
public sealed class ServerClockObserver : IServerClockObserver
{
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private ServerClockObservation? _latest;

    /// <summary>Initialises a new instance.</summary>
    public ServerClockObserver(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public void Record(DateTimeOffset serverTime)
    {
        var deviceNow = _clock.GetUtcNow();
        var skew = deviceNow - serverTime;
        lock (_gate)
        {
            _latest = new ServerClockObservation(deviceNow, serverTime, skew);
        }
    }

    /// <inheritdoc />
    public ServerClockObservation? GetLatest()
    {
        lock (_gate) { return _latest; }
    }

    /// <inheritdoc />
    public TimeSpan GetSkew()
        => GetLatest()?.Skew ?? TimeSpan.Zero;
}
