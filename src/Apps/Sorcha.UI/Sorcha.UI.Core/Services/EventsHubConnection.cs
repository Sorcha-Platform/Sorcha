// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Manages SignalR connection to the Events Hub for real-time activity event notifications.
/// </summary>
/// <remarks>
/// Connects to the Blueprint Service EventsHub at /hubs/events
///
/// Events received from server:
/// - EventReceived: New activity event pushed in real-time
/// - UnreadCountUpdated: Updated unread count for the user
///
/// Client can subscribe to:
/// - Personal events (Subscribe)
/// - Organisation events (SubscribeOrg, admin only)
/// </remarks>
public class EventsHubConnection : IAsyncDisposable
{
    private readonly ILogger<EventsHubConnection> _logger;
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configurationService;
    private readonly string _hubUrl;
    private HubConnection? _hubConnection;
    private ConnectionState _connectionState = new();

    /// <summary>
    /// Event raised when a new activity event is received.
    /// </summary>
    public event Action<ActivityEventDto>? OnEventReceived;

    /// <summary>
    /// Event raised when the unread count is updated.
    /// </summary>
    public event Action<int>? OnUnreadCountUpdated;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event Action<ConnectionState>? OnConnectionStateChanged;

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Creates a new EventsHubConnection.
    /// </summary>
    /// <param name="baseUrl">Base URL of the API Gateway (e.g., https://localhost)</param>
    /// <param name="authService">Authentication service for JWT token retrieval</param>
    /// <param name="configurationService">Configuration service for active profile</param>
    /// <param name="logger">Logger for diagnostics</param>
    public EventsHubConnection(
        string baseUrl,
        IAuthenticationService authService,
        IConfigurationService configurationService,
        ILogger<EventsHubConnection> logger)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/events";
        _authService = authService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Starts the SignalR connection and subscribes to personal events.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection != null)
        {
            _logger.LogDebug("EventsHub connection already exists");
            return;
        }

        UpdateConnectionState(ConnectionStatus.Connecting);

        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var profileName = await _configurationService.GetActiveProfileNameAsync();
                        var token = await _authService.GetAccessTokenAsync(profileName);
                        _logger.LogDebug("EventsHub token provider: profile={Profile}, hasToken={HasToken}",
                            profileName, !string.IsNullOrEmpty(token));
                        return token;
                    };
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

            // Register event handlers for server-to-client calls
            RegisterEventHandlers();

            // Handle connection lifecycle
            _hubConnection.Reconnecting += error =>
            {
                _logger.LogWarning("EventsHub reconnecting: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Reconnecting, error?.Message);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async connectionId =>
            {
                _logger.LogInformation("EventsHub reconnected: {ConnectionId}", connectionId);
                UpdateConnectionState(ConnectionStatus.Connected);

                // Re-subscribe to personal events after reconnection
                await SubscribeAsync();
            };

            _hubConnection.Closed += error =>
            {
                _logger.LogWarning("EventsHub connection closed: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Disconnected, error?.Message);
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync(cancellationToken);

            UpdateConnectionState(ConnectionStatus.Connected);
            _logger.LogInformation("EventsHub connected to {HubUrl}", _hubUrl);

            // Auto-subscribe to personal events
            await SubscribeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to EventsHub at {HubUrl}", _hubUrl);
            UpdateConnectionState(ConnectionStatus.Disconnected, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Stops the SignalR connection.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null)
        {
            return;
        }

        try
        {
            await _hubConnection.StopAsync(cancellationToken);
            UpdateConnectionState(ConnectionStatus.Disconnected);
            _logger.LogInformation("EventsHub disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping EventsHub connection");
        }
    }

    /// <summary>
    /// Subscribes to personal events on the hub.
    /// </summary>
    private async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot subscribe to events: not connected");
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("Subscribe", cancellationToken);
            _logger.LogDebug("Subscribed to personal events");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to personal events");
        }
    }

    /// <summary>
    /// Registers all event handlers for server-to-client SignalR calls.
    /// </summary>
    private void RegisterEventHandlers()
    {
        if (_hubConnection == null) return;

        // EventReceived - new activity event
        _hubConnection.On<ActivityEventDto>("EventReceived", eventDto =>
        {
            _logger.LogDebug(
                "Event received: Id={EventId}, Type={EventType}, Title={Title}",
                eventDto.Id,
                eventDto.EventType,
                eventDto.Title);

            OnEventReceived?.Invoke(eventDto);
        });

        // UnreadCountUpdated - updated unread count
        _hubConnection.On<int>("UnreadCountUpdated", count =>
        {
            _logger.LogDebug("Unread count updated: {Count}", count);

            OnUnreadCountUpdated?.Invoke(count);
        });
    }

    /// <summary>
    /// Updates the connection state and notifies subscribers.
    /// </summary>
    private void UpdateConnectionState(ConnectionStatus status, string? errorMessage = null)
    {
        var newState = status switch
        {
            ConnectionStatus.Connected => new ConnectionState
            {
                Status = status,
                LastConnected = DateTime.UtcNow,
                ReconnectAttempts = 0
            },
            ConnectionStatus.Reconnecting => _connectionState with
            {
                Status = status,
                ReconnectAttempts = _connectionState.ReconnectAttempts + 1,
                ErrorMessage = errorMessage
            },
            _ => new ConnectionState
            {
                Status = status,
                LastConnected = _connectionState.LastConnected,
                ReconnectAttempts = _connectionState.ReconnectAttempts,
                ErrorMessage = errorMessage
            }
        };

        _connectionState = newState;
        OnConnectionStateChanged?.Invoke(newState);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        GC.SuppressFinalize(this);
    }
}
