// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Services;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Default <see cref="IPresentationSignal"/> implementation backed by
/// <see cref="PresentationHubConnection"/> (SignalR primary) and a periodic poll
/// against F111's <c>GET /api/presentations/{id}/status</c> endpoint
/// (fallback). Mirrors F126's <c>EnrolPairingSignal</c> shape.
/// </summary>
public sealed class PresentationSignal : IPresentationSignal, IAsyncDisposable
{
    private static readonly TimeSpan HubConnectWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollingCadence = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ManualRecoveryWindow = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.Ordinal)
    {
        "success", "decline", "abandoned", "abandoned-with-late-outcome", "expired"
    };

    private readonly PresentationHubConnection _hub;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger<PresentationSignal> _logger;

    private Guid _presentationRequestId;
    private string _groupName = string.Empty;
    private CancellationTokenSource? _cts;
    private Task? _pollingLoop;
    private Task? _manualRecoveryTimer;
    private bool _signalReceived;

    public event Func<PresentationSignalOutcome, Task>? OnOutcomeReady;
    public event Action? OnFallbackEngaged;
    public event Action? OnManualRecoveryRequired;

    public PresentationSignal(
        PresentationHubConnection hub,
        HttpClient http,
        TimeProvider timeProvider,
        ILogger<PresentationSignal> logger)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(Guid presentationRequestId, CancellationToken ct)
    {
        if (presentationRequestId == Guid.Empty)
        {
            throw new ArgumentException("presentationRequestId must be non-empty.", nameof(presentationRequestId));
        }

        _presentationRequestId = presentationRequestId;
        _groupName = $"presentation:{presentationRequestId:N}";
        _signalReceived = false;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _hub.OnPresentationOutcomeReady += HandleHubOutcomeReady;

        // Manual-recovery timer runs regardless of transport — fires after 60 s
        // unless a terminal signal has arrived.
        _manualRecoveryTimer = StartManualRecoveryTimerAsync(_cts.Token);

        // Try to connect the hub; if it doesn't connect within 2 s, engage polling.
        var hubStart = _hub.StartAsync(_cts.Token);
        var deadline = Task.Delay(HubConnectWindow, _time, _cts.Token);
        var winner = await Task.WhenAny(hubStart, deadline).ConfigureAwait(false);

        await hubStart.ConfigureAwait(false);

        // If the hub is up, join the per-presentation group so we receive
        // PresentationOutcomeReady for this request specifically.
        if (_hub.IsConnected)
        {
            await _hub.JoinGroupAsync(_groupName, _cts.Token).ConfigureAwait(false);
        }

        // Always start the polling loop in the background: a fresh hub may
        // miss the first event due to subscribe-race, and a late-arriving
        // event would otherwise hang the council page. The loop self-terminates
        // when a terminal signal is received.
        //
        // Determine engageReason — distinguishes "hub never connected" from
        // "hub healthy, polling running for safety":
        //   * winner == deadline → hub-connect timed out; polling is the primary.
        //   * winner == hubStart but !_hub.IsConnected → hub failed fast (the
        //     typical council-page case where the hub URL isn't routable);
        //     polling is the primary.
        //   * winner == hubStart AND _hub.IsConnected → hub is up; polling is
        //     belt-and-braces for late events. No fallback signal needed.
        var engageReason = winner == hubStart
            ? (_hub.IsConnected ? null : "hub-connect-failed")
            : "hub-timeout";
        _pollingLoop = PollingLoopAsync(engageReason, _cts.Token);
    }

    public Task StopAsync() => StopInternalAsync();

    private async Task StopInternalAsync()
    {
        _hub.OnPresentationOutcomeReady -= HandleHubOutcomeReady;
        if (!string.IsNullOrEmpty(_groupName) && _hub.IsConnected)
        {
            try { await _hub.LeaveGroupAsync(_groupName); }
            catch { /* non-fatal */ }
        }

        try { _cts?.Cancel(); }
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

    private async Task HandleHubOutcomeReady(string presentationRequestIdRaw)
    {
        if (!Guid.TryParseExact(presentationRequestIdRaw, "N", out var requestId) &&
            !Guid.TryParse(presentationRequestIdRaw, out requestId))
        {
            return;
        }
        if (requestId != _presentationRequestId) return;

        // Hub signal is thin — fetch the lifecycle state to learn the kind.
        var kind = await FetchOutcomeKindAsync(CancellationToken.None).ConfigureAwait(false);
        if (kind is not null && TerminalStates.Contains(kind))
        {
            await RaiseOutcomeAsync(kind).ConfigureAwait(false);
        }
    }

    private async Task RaiseOutcomeAsync(string kind)
    {
        if (_signalReceived) return;
        _signalReceived = true;
        var handler = OnOutcomeReady;
        if (handler is not null)
        {
            try
            {
                await handler.Invoke(new PresentationSignalOutcome(_presentationRequestId, kind)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnOutcomeReady handler threw");
            }
        }
    }

    private async Task PollingLoopAsync(string? engageReason, CancellationToken ct)
    {
        if (engageReason is not null)
        {
            _logger.LogInformation("Presentation-signal polling engaged ({Reason})", engageReason);
            OnFallbackEngaged?.Invoke();
        }

        while (!ct.IsCancellationRequested && !_signalReceived)
        {
            try
            {
                var kind = await FetchOutcomeKindAsync(ct).ConfigureAwait(false);
                if (kind is not null && TerminalStates.Contains(kind))
                {
                    await RaiseOutcomeAsync(kind).ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Presentation-signal poll failed; will retry");
            }

            try { await Task.Delay(PollingCadence, _time, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<string?> FetchOutcomeKindAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"/api/presentations/{_presentationRequestId:D}/status", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var payload = await response.Content
                .ReadFromJsonAsync<StatusProbeShape>(ct)
                .ConfigureAwait(false);
            return payload?.State;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception)
        {
            // Surface as no-signal; the polling loop logs at debug.
            return null;
        }
    }

    private async Task StartManualRecoveryTimerAsync(CancellationToken ct)
    {
        try { await Task.Delay(ManualRecoveryWindow, _time, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        if (!_signalReceived)
        {
            _logger.LogInformation("Presentation-signal manual-recovery window elapsed");
            OnManualRecoveryRequired?.Invoke();
        }
    }

    private sealed record StatusProbeShape
    {
        [JsonPropertyName("state")] public string? State { get; init; }
    }
}
