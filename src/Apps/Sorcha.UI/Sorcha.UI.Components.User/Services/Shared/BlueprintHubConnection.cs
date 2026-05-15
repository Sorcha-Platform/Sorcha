// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// SignalR client wrapper for the Blueprint Service notification hub at
/// <c>/hubs/blueprint</c>. Feature 127 surfaces the
/// <c>PresentationOutcomeReady</c> event a council page subscribes to after
/// initiating a credential-gated action.
/// </summary>
/// <remarks>
/// <para>Models <see cref="TenantHubConnection"/>. The Blueprint hub's
/// presentation-group subscription is keyed by <c>presentationRequestId</c>
/// (high-entropy nonce) rather than the user, so the wrapper accepts an
/// OPTIONAL access-token provider — passing <c>null</c> connects
/// unauthenticated, which is the council-page case where the page itself has
/// no user session.</para>
/// <para>The reconnect schedule (0/2/5/10/30 s) mirrors the other hub
/// wrappers in this library.</para>
/// </remarks>
public sealed class BlueprintHubConnection : IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly Func<Task<string?>>? _accessTokenProvider;
    private readonly ILogger<BlueprintHubConnection> _logger;

    private HubConnection? _hubConnection;
    private readonly HashSet<string> _subscribedGroups = new(StringComparer.Ordinal);

    /// <summary>
    /// Fires when the server emits <c>PresentationOutcomeReady(presentationRequestId)</c>.
    /// Feature 127 — council pages subscribed to
    /// <c>BlueprintHubGroups.PresentationNonce</c> use this signal to fetch
    /// disclosed claims for autofill.
    /// </summary>
    public event Func<string, Task>? OnPresentationOutcomeReady;

    /// <summary>
    /// Initialises the wrapper. <paramref name="baseUrl"/> is the gateway origin
    /// (e.g. <c>http://localhost</c>); the wrapper appends <c>/hubs/blueprint</c>.
    /// Pass <paramref name="accessTokenProvider"/> = <c>null</c> for
    /// unauthenticated council-page subscriptions.
    /// </summary>
    public BlueprintHubConnection(
        string baseUrl,
        Func<Task<string?>>? accessTokenProvider,
        ILogger<BlueprintHubConnection> logger)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/blueprint";
        _accessTokenProvider = accessTokenProvider;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether the underlying SignalR connection is currently connected.</summary>
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    /// <summary>Starts the SignalR connection. Idempotent.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection is not null)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
                return;
            await DisposeHubConnectionAsync();
        }

        try
        {
            var builder = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    if (_accessTokenProvider is not null)
                    {
                        options.AccessTokenProvider = _accessTokenProvider;
                    }
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                });

            _hubConnection = builder.Build();

            _hubConnection.On<string>("PresentationOutcomeReady", async (presentationRequestId) =>
            {
                _logger.LogDebug(
                    "BlueprintHub PresentationOutcomeReady: {RequestId}", presentationRequestId);
                if (OnPresentationOutcomeReady is not null)
                {
                    try { await OnPresentationOutcomeReady(presentationRequestId); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "OnPresentationOutcomeReady handler threw");
                    }
                }
            });

            _hubConnection.Reconnecting += error =>
            {
                _logger.LogWarning("BlueprintHub reconnecting: {Error}", error?.Message);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += _ =>
            {
                _logger.LogInformation("BlueprintHub reconnected; resubscribing to {Count} group(s)", _subscribedGroups.Count);
                return ResubscribeGroupsAsync();
            };

            await _hubConnection.StartAsync(cancellationToken);
            _logger.LogInformation("BlueprintHub connected to {HubUrl}", _hubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to BlueprintHub at {HubUrl}; the F111 status-poll fallback path remains available", _hubUrl);
        }
    }

    /// <summary>
    /// Join a group. Tracked locally so an automatic reconnect can re-join.
    /// </summary>
    public async Task JoinGroupAsync(string groupName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        _subscribedGroups.Add(groupName);
        if (_hubConnection is { State: HubConnectionState.Connected })
        {
            try
            {
                await _hubConnection.InvokeAsync("JoinGroup", groupName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to invoke JoinGroup({Group}) on BlueprintHub; will retry on reconnect", groupName);
            }
        }
    }

    /// <summary>Leave a group, both server-side and from local tracking.</summary>
    public async Task LeaveGroupAsync(string groupName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        _subscribedGroups.Remove(groupName);
        if (_hubConnection is { State: HubConnectionState.Connected })
        {
            try
            {
                await _hubConnection.InvokeAsync("LeaveGroup", groupName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Failed to invoke LeaveGroup({Group}) on BlueprintHub; non-fatal", groupName);
            }
        }
    }

    /// <summary>Stops and disposes the connection.</summary>
    public async Task StopAsync()
    {
        if (_hubConnection is null) return;
        try { await _hubConnection.StopAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error stopping BlueprintHub"); }
        await DisposeHubConnectionAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private async Task DisposeHubConnectionAsync()
    {
        if (_hubConnection is null) return;
        try { await _hubConnection.DisposeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing BlueprintHub"); }
        _hubConnection = null;
    }

    private async Task ResubscribeGroupsAsync()
    {
        if (_hubConnection is not { State: HubConnectionState.Connected }) return;
        foreach (var group in _subscribedGroups.ToList())
        {
            try { await _hubConnection.InvokeAsync("JoinGroup", group); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to rejoin {Group} after reconnect", group); }
        }
    }
}
