// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;

namespace Sorcha.ServiceDefaults.Hubs;

/// <summary>
/// Wraps a <see cref="HubConnection" /> and exposes a connection-state
/// observable plus a poll-now hint, so UI surfaces can fall back to REST
/// polling when the websocket disconnects beyond the reconnect window.
/// </summary>
/// <remarks>
/// <para>
/// Per Feature 118 spec FR-033 / FR-034 and research R-008. Default poll
/// cadence is 15 s with ±20 % jitter, engaging after 90 s of failed
/// reconnect (three failed reconnect attempts at the 30 s tail of the
/// nominal schedule).
/// </para>
/// <para>
/// The wrapper does not own the <see cref="HubConnection" /> lifecycle —
/// callers construct the connection via
/// <see cref="ServiceClients.Http.Hub.SorchaHubConnectionBuilder" /> and
/// hand it to this wrapper. <see cref="DisposeAsync" /> stops polling but
/// does not dispose the underlying connection.
/// </para>
/// </remarks>
public sealed class HubConnectionWithFallback : IAsyncDisposable
{
    /// <summary>Default poll cadence when the websocket is down.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait after an unrecoverable disconnect before activating polling.</summary>
    public static readonly TimeSpan DefaultEngageAfter = TimeSpan.FromSeconds(90);

    private const double JitterFraction = 0.20;

    private readonly HubConnection _connection;
    private readonly Func<CancellationToken, Task>? _pollFallback;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _engageAfter;
    private readonly Func<double> _randomNextDouble;

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private DateTimeOffset? _disconnectedAt;
    private readonly object _stateLock = new();

    /// <summary>Raised whenever the underlying hub connection state changes.</summary>
    public event Action<HubConnectionState>? ConnectionStateChanged;

    /// <summary>True while the polling fallback is actively firing.</summary>
    public bool IsPolling { get; private set; }

    /// <summary>Underlying hub connection. Use to attach event handlers and invoke server methods.</summary>
    public HubConnection Connection => _connection;

    /// <summary>
    /// Constructs the wrapper around an existing hub connection.
    /// </summary>
    /// <param name="connection">The hub connection — already built but not necessarily started.</param>
    /// <param name="pollFallback">Optional REST refresher. When the connection has been disconnected longer than <paramref name="engageAfter" />, this delegate is invoked at <paramref name="pollInterval" /> until the connection recovers. <c>null</c> disables polling fallback (the wrapper still surfaces connection-state events).</param>
    /// <param name="pollInterval">Poll cadence. Defaults to <see cref="DefaultPollInterval" />.</param>
    /// <param name="engageAfter">Quiet period before polling activates. Defaults to <see cref="DefaultEngageAfter" />.</param>
    public HubConnectionWithFallback(
        HubConnection connection,
        Func<CancellationToken, Task>? pollFallback = null,
        TimeSpan? pollInterval = null,
        TimeSpan? engageAfter = null)
        : this(connection, pollFallback, pollInterval, engageAfter, randomNextDouble: null)
    {
    }

    // Internal constructor for deterministic tests.
    internal HubConnectionWithFallback(
        HubConnection connection,
        Func<CancellationToken, Task>? pollFallback,
        TimeSpan? pollInterval,
        TimeSpan? engageAfter,
        Func<double>? randomNextDouble)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        _pollFallback = pollFallback;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _engageAfter = engageAfter ?? DefaultEngageAfter;
        _randomNextDouble = randomNextDouble ?? (static () => Random.Shared.NextDouble());

        _connection.Reconnecting += OnReconnecting;
        _connection.Reconnected += OnReconnected;
        _connection.Closed += OnClosed;
    }

    /// <summary>
    /// Manually mark the underlying connection as disconnected — used by
    /// tests and call sites that detect disconnection out-of-band (e.g.,
    /// the connection has not yet been started).
    /// </summary>
    public void NotifyDisconnected()
    {
        SetDisconnectTimestamp(DateTimeOffset.UtcNow);
        EnsurePollingTaskStarted();
        ConnectionStateChanged?.Invoke(HubConnectionState.Disconnected);
    }

    /// <summary>
    /// Manually mark the underlying connection as connected.
    /// </summary>
    public void NotifyConnected()
    {
        ClearDisconnectTimestamp();
        StopPolling();
        ConnectionStateChanged?.Invoke(HubConnectionState.Connected);
    }

    private Task OnReconnecting(Exception? exception)
    {
        SetDisconnectTimestamp(DateTimeOffset.UtcNow);
        EnsurePollingTaskStarted();
        ConnectionStateChanged?.Invoke(HubConnectionState.Reconnecting);
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        ClearDisconnectTimestamp();
        StopPolling();
        ConnectionStateChanged?.Invoke(HubConnectionState.Connected);
        return Task.CompletedTask;
    }

    private Task OnClosed(Exception? exception)
    {
        SetDisconnectTimestamp(DateTimeOffset.UtcNow);
        EnsurePollingTaskStarted();
        ConnectionStateChanged?.Invoke(HubConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    private void SetDisconnectTimestamp(DateTimeOffset at)
    {
        lock (_stateLock)
        {
            _disconnectedAt ??= at;
        }
    }

    private void ClearDisconnectTimestamp()
    {
        lock (_stateLock)
        {
            _disconnectedAt = null;
        }
    }

    private void EnsurePollingTaskStarted()
    {
        if (_pollFallback is null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_pollTask is not null && !_pollTask.IsCompleted)
            {
                return;
            }
            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token));
        }
    }

    private void StopPolling()
    {
        CancellationTokenSource? cts;
        lock (_stateLock)
        {
            cts = _pollCts;
            _pollCts = null;
        }
        cts?.Cancel();
        IsPolling = false;
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        // Wait the engage-after window before activating. If the connection
        // recovers in that time, _pollCts is cancelled and we exit cleanly.
        try
        {
            await Task.Delay(_engageAfter, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        IsPolling = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _pollFallback!(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Fallback delegate is responsible for its own logging. We
                // continue polling so transient failures don't kill the loop.
            }

            try
            {
                await Task.Delay(JitteredInterval(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan JitteredInterval()
    {
        var multiplier = 1.0 + (_randomNextDouble() * 2.0 - 1.0) * JitterFraction;
        return TimeSpan.FromMilliseconds(_pollInterval.TotalMilliseconds * multiplier);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _connection.Reconnecting -= OnReconnecting;
        _connection.Reconnected -= OnReconnected;
        _connection.Closed -= OnClosed;
        StopPolling();
        return ValueTask.CompletedTask;
    }
}
