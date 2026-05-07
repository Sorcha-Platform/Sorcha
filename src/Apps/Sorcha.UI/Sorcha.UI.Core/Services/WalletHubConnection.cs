// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Manages SignalR connection to the Wallet Service hub at <c>/hubs/wallet</c>
/// for real-time wallet-domain notifications.
/// </summary>
/// <remarks>
/// <para>
/// Surface today (Feature 114 citizen wallet):
/// - <see cref="OnDeviceRevoked"/> — citizen device was revoked
/// - <see cref="OnCredentialAvailable"/> — a new credential is available for sync
/// </para>
/// <para>
/// Future surface (per <c>contracts/wallet-hub-client.cs.md</c>): transaction
/// lifecycle (<c>TransactionReceived</c>/<c>TransactionConfirmed</c>/<c>TransactionReceipted</c>),
/// encryption pipeline (<c>EncryptionProgress</c>/<c>Complete</c>/<c>Failed</c>), and
/// org-credential events (<c>CredentialReceived</c>/<c>CredentialStatusChanged</c>/
/// <c>PendingCredentialCountUpdated</c>) will land here as the server-side emit
/// sites migrate off <see cref="BlueprintHubConnection"/>.
/// </para>
/// <para>
/// All notifications conform to the Feature 118 thin-signal contract — opaque IDs
/// and timestamps only. Clients pull full detail through authenticated REST.
/// </para>
/// </remarks>
public class WalletHubConnection : IAsyncDisposable
{
    private readonly ILogger<WalletHubConnection> _logger;
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configurationService;
    private readonly string _hubUrl;
    private HubConnection? _hubConnection;
    private ConnectionState _connectionState = new();

    /// <summary>Citizen device was revoked (admin- or self-initiated).</summary>
    public event Action<Guid>? OnDeviceRevoked;

    /// <summary>A new credential is available for the citizen's wallet to sync.</summary>
    public event Action<string>? OnCredentialAvailable;

    /// <summary>Connection state changed.</summary>
    public event Action<ConnectionState>? OnConnectionStateChanged;

    /// <summary>Current connection state.</summary>
    public ConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Creates a new <see cref="WalletHubConnection"/>.
    /// </summary>
    /// <param name="baseUrl">Base URL of the Wallet Service or API Gateway.</param>
    /// <param name="authService">Authentication service for JWT token retrieval.</param>
    /// <param name="configurationService">Configuration service for active profile.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public WalletHubConnection(
        string baseUrl,
        IAuthenticationService authService,
        IConfigurationService configurationService,
        ILogger<WalletHubConnection> logger)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/wallet";
        _authService = authService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>Starts the SignalR connection.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection != null)
        {
            _logger.LogDebug("WalletHub connection already exists");
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
                        _logger.LogDebug("WalletHub token provider: profile={Profile}, hasToken={HasToken}",
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

            RegisterEventHandlers();

            _hubConnection.Reconnecting += error =>
            {
                _logger.LogWarning("WalletHub reconnecting: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Reconnecting, error?.Message);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += connectionId =>
            {
                _logger.LogInformation("WalletHub reconnected: {ConnectionId}", connectionId);
                UpdateConnectionState(ConnectionStatus.Connected);
                return Task.CompletedTask;
            };

            _hubConnection.Closed += error =>
            {
                _logger.LogWarning("WalletHub connection closed: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Disconnected, error?.Message);
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync(cancellationToken);

            UpdateConnectionState(ConnectionStatus.Connected);
            _logger.LogInformation("WalletHub connected to {HubUrl}", _hubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to WalletHub at {HubUrl}", _hubUrl);
            UpdateConnectionState(ConnectionStatus.Disconnected, ex.Message);
            throw;
        }
    }

    /// <summary>Stops the SignalR connection.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null) return;

        try
        {
            await _hubConnection.StopAsync(cancellationToken);
            UpdateConnectionState(ConnectionStatus.Disconnected);
            _logger.LogInformation("WalletHub disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping WalletHub connection");
        }
    }

    private void RegisterEventHandlers()
    {
        if (_hubConnection == null) return;

        // Feature 114 — DeviceRevoked carries a single Guid deviceId.
        _hubConnection.On<Guid>("DeviceRevoked", deviceId =>
        {
            _logger.LogDebug("WalletHub DeviceRevoked: {DeviceId}", deviceId);
            OnDeviceRevoked?.Invoke(deviceId);
        });

        // Feature 114 — CredentialAvailable carries a single credentialId string.
        _hubConnection.On<string>("CredentialAvailable", credentialId =>
        {
            _logger.LogDebug("WalletHub CredentialAvailable: {CredentialId}", credentialId);
            OnCredentialAvailable?.Invoke(credentialId);
        });
    }

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
