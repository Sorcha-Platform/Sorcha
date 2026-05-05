// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// SignalR client wrapper for the Tenant Service inbox hub at <c>/hubs/tenant</c>.
/// Phase 5 of Feature 118 — surfaces durable inbox events to the UI bell + activity panel.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="RegisterHubConnection"/>. Two events are exposed today:
/// <see cref="OnInboxEntryAdded"/> and <see cref="OnInboxUnreadCountUpdated"/>.
/// Membership / security / system announcement events arrive in later phases.
/// </para>
/// <para>
/// JWT auth via the standard <c>?access_token=</c> query parameter; the
/// <c>platform_user_id</c> claim is required by the server. Reconnect schedule
/// matches the other hub wrappers (0/2/5/10/30s).
/// </para>
/// </remarks>
public sealed class TenantHubConnection : IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly Func<Task<string?>> _accessTokenProvider;
    private readonly ILogger<TenantHubConnection> _logger;

    private HubConnection? _hubConnection;
    private ConnectionState _connectionState = new();

    /// <summary>Fires when the server emits <c>InboxEntryAdded(entryId, occurredAt, traceId)</c>.</summary>
    public event Func<string, DateTimeOffset, string, Task>? OnInboxEntryAdded;

    /// <summary>Fires when the server emits <c>InboxUnreadCountUpdated(unreadCount, occurredAt, traceId)</c>.</summary>
    public event Func<int, DateTimeOffset, string, Task>? OnInboxUnreadCountUpdated;

    /// <summary>Connection-state changes for the inline reconnect indicator.</summary>
    public event Action<ConnectionState>? OnConnectionStateChanged;

    /// <summary>Current connection state.</summary>
    public ConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Initialises the wrapper. <paramref name="baseUrl"/> is the gateway origin
    /// (e.g. <c>http://localhost</c>); the wrapper appends <c>/hubs/tenant</c>.
    /// </summary>
    public TenantHubConnection(
        string baseUrl,
        Func<Task<string?>> accessTokenProvider,
        ILogger<TenantHubConnection> logger)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/tenant";
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the SignalR connection. If a dead connection exists, it is disposed first.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection is not null)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
                return;
            await DisposeHubConnectionAsync();
        }

        UpdateConnectionState(ConnectionStatus.Connecting);

        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    options.AccessTokenProvider = _accessTokenProvider;
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            _hubConnection.On<string, DateTimeOffset, string>("InboxEntryAdded", async (entryId, occurredAt, traceId) =>
            {
                _logger.LogDebug("Inbox entry added: {EntryId}", entryId);
                if (OnInboxEntryAdded is not null)
                {
                    await OnInboxEntryAdded(entryId, occurredAt, traceId);
                }
            });

            _hubConnection.On<int, DateTimeOffset, string>("InboxUnreadCountUpdated", async (unreadCount, occurredAt, traceId) =>
            {
                _logger.LogDebug("Inbox unread count updated: {Count}", unreadCount);
                if (OnInboxUnreadCountUpdated is not null)
                {
                    await OnInboxUnreadCountUpdated(unreadCount, occurredAt, traceId);
                }
            });

            _hubConnection.Reconnecting += error =>
            {
                _logger.LogWarning("TenantHub reconnecting: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Reconnecting, error?.Message);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += _ =>
            {
                _logger.LogInformation("TenantHub reconnected");
                UpdateConnectionState(ConnectionStatus.Connected);
                return Task.CompletedTask;
            };

            _hubConnection.Closed += error =>
            {
                _logger.LogWarning("TenantHub closed: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Disconnected, error?.Message);
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync(cancellationToken);
            UpdateConnectionState(ConnectionStatus.Connected);
            _logger.LogInformation("TenantHub connected to {HubUrl}", _hubUrl);
        }
        catch (Exception ex)
        {
            UpdateConnectionState(ConnectionStatus.Disconnected, ex.Message);
            _logger.LogError(ex, "Failed to connect to TenantHub at {HubUrl}", _hubUrl);
        }
    }

    /// <summary>Stops and disposes the connection.</summary>
    public async Task StopAsync()
    {
        if (_hubConnection is null) return;
        try
        {
            await _hubConnection.StopAsync();
            _logger.LogInformation("TenantHub disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping TenantHub connection");
        }
        await DisposeHubConnectionAsync();
        UpdateConnectionState(ConnectionStatus.Disconnected);
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
        catch (Exception ex) { _logger.LogWarning(ex, "Error disposing TenantHub connection"); }
        _hubConnection = null;
    }

    private void UpdateConnectionState(ConnectionStatus status, string? errorMessage = null)
    {
        var prev = _connectionState;
        _connectionState = _connectionState with
        {
            Status = status,
            ErrorMessage = errorMessage,
            LastConnected = status == ConnectionStatus.Connected ? DateTime.UtcNow : prev.LastConnected,
            ReconnectAttempts = status == ConnectionStatus.Reconnecting ? prev.ReconnectAttempts + 1 : 0,
        };
        OnConnectionStateChanged?.Invoke(_connectionState);
    }
}
