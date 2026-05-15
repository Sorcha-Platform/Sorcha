// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Services;

namespace Sorcha.UI.Core.Services.User.Enrolment;

/// <summary>
/// <see cref="IEnrolPairingSignal"/> backed by <see cref="TenantHubConnection"/>
/// for the SignalR path and a periodic <c>/api/v1/me/devices</c> poll for the
/// fallback path. Cadence comes from research §R-006:
/// <list type="bullet">
///   <item><description>2 s hub-connect timeout before polling engages</description></item>
///   <item><description>3 s polling cadence</description></item>
///   <item><description>60 s ceiling before manual-recovery affordance fires</description></item>
/// </list>
/// </summary>
public sealed class EnrolPairingSignal : IEnrolPairingSignal, IAsyncDisposable
{
    private static readonly TimeSpan HubConnectWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollingCadence = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ManualRecoveryWindow = TimeSpan.FromSeconds(60);

    private readonly TenantHubConnection _hub;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger<EnrolPairingSignal> _logger;

    private Guid _platformUserId;
    private CancellationTokenSource? _cts;
    private Task? _pollingLoop;
    private Task? _manualRecoveryTimer;
    private bool _signalReceived;

    public event Func<Guid, Task>? OnDeviceEnrolled;
    public event Action? OnFallbackEngaged;
    public event Action? OnManualRecoveryRequired;

    public EnrolPairingSignal(
        TenantHubConnection hub,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger<EnrolPairingSignal> logger)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(Guid platformUserId, CancellationToken ct)
    {
        if (platformUserId == Guid.Empty)
        {
            throw new ArgumentException("PlatformUserId must be non-empty.", nameof(platformUserId));
        }

        _platformUserId = platformUserId;
        _signalReceived = false;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _hub.OnDeviceEnrolled += HandleHubDeviceEnrolled;

        // Manual-recovery timer runs regardless of transport — fires after 60 s
        // unless a signal has arrived.
        _manualRecoveryTimer = StartManualRecoveryTimerAsync(_cts.Token);

        // Try to connect the hub; if it doesn't connect within 2 s, engage polling.
        var hubStart = _hub.StartAsync(_cts.Token);
        var deadline = Task.Delay(HubConnectWindow, _cts.Token);
        var winner = await Task.WhenAny(hubStart, deadline).ConfigureAwait(false);

        // Even if hub connects, kick off polling at the same cadence so a
        // late-arriving signal doesn't get lost when the hub's WS later flaps.
        // The polling loop is a no-op once a signal is received.
        _pollingLoop = PollingLoopAsync(winner == hubStart ? null : "hub-timeout", _cts.Token);

        await hubStart.ConfigureAwait(false);
    }

    public Task StopAsync() => StopInternalAsync();

    private async Task StopInternalAsync()
    {
        _hub.OnDeviceEnrolled -= HandleHubDeviceEnrolled;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException) { }

        if (_pollingLoop is not null)
        {
            try { await _pollingLoop.ConfigureAwait(false); } catch { /* expected on cancel */ }
            _pollingLoop = null;
        }
        if (_manualRecoveryTimer is not null)
        {
            try { await _manualRecoveryTimer.ConfigureAwait(false); } catch { /* expected on cancel */ }
            _manualRecoveryTimer = null;
        }
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopInternalAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private Task HandleHubDeviceEnrolled(Guid platformUserId, Guid deviceId)
    {
        if (platformUserId != _platformUserId)
        {
            return Task.CompletedTask;
        }
        return RaiseDeviceEnrolledAsync(deviceId);
    }

    private async Task RaiseDeviceEnrolledAsync(Guid deviceId)
    {
        if (_signalReceived) return;
        _signalReceived = true;
        var handler = OnDeviceEnrolled;
        if (handler is not null)
        {
            try { await handler.Invoke(deviceId).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "OnDeviceEnrolled handler threw"); }
        }
    }

    private async Task PollingLoopAsync(string? engageReason, CancellationToken ct)
    {
        if (engageReason is not null)
        {
            _logger.LogInformation("Enrol pairing-signal polling engaged ({Reason})", engageReason);
            OnFallbackEngaged?.Invoke();
        }

        try
        {
            while (!ct.IsCancellationRequested && !_signalReceived)
            {
                try
                {
                    using var response = await _http.GetAsync("/api/v1/me/devices", ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var payload = await response.Content
                            .ReadFromJsonAsync<DeviceListProbeShape>(ct)
                            .ConfigureAwait(false);
                        var active = payload?.ActiveCount
                            ?? payload?.Devices?.Count(d => string.Equals(d.Status, "active", StringComparison.OrdinalIgnoreCase))
                            ?? 0;
                        if (active > 0)
                        {
                            await RaiseDeviceEnrolledAsync(Guid.Empty).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Enrol pairing-signal poll failed; will retry");
                }

                try { await Task.Delay(PollingCadence, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
        finally
        {
            // intentional no-op
        }
    }

    private async Task StartManualRecoveryTimerAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(ManualRecoveryWindow, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }

        if (!_signalReceived)
        {
            _logger.LogInformation("Enrol pairing-signal manual-recovery window elapsed");
            OnManualRecoveryRequired?.Invoke();
        }
    }

    private sealed record DeviceListProbeShape(int? ActiveCount, IReadOnlyList<DeviceProbeRow>? Devices);
    private sealed record DeviceProbeRow(string? Status);
}
