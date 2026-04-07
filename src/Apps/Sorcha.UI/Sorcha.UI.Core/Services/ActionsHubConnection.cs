// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Actions;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Manages SignalR connection to the Actions Hub for real-time action notifications.
/// </summary>
/// <remarks>
/// Connects to the Blueprint Service ActionsHub at /actionshub
///
/// Events received from server:
/// - ActionAvailable: New action available for a participant
/// - ActionConfirmed: Action has been confirmed/completed
/// - ActionRejected: Action was rejected and routed elsewhere
/// - WorkflowCompleted: Entire workflow has completed
/// - EncryptionProgress: Encryption operation progress signal
/// - EncryptionComplete: Encryption operation completed signal
/// - EncryptionFailed: Encryption operation failed signal
///
/// All notifications use thin signal types (SignalNotification / EncryptionSignal)
/// that carry only identifiers — UI pulls details through REST endpoints.
///
/// Client can subscribe to:
/// - Wallet-based notifications (SubscribeToWallet)
/// - Instance-based notifications (future)
/// </remarks>
public class ActionsHubConnection : IAsyncDisposable
{
    private readonly ILogger<ActionsHubConnection> _logger;
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configurationService;
    private readonly string _hubUrl;
    private HubConnection? _hubConnection;
    private ConnectionState _connectionState = new();
    private readonly HashSet<string> _subscribedWallets = [];

    /// <summary>
    /// Event raised when a new action is available.
    /// Parameters: SignalNotification with SignalType "action-available"
    /// </summary>
    public event Func<SignalNotification, Task>? OnActionAvailable;

    /// <summary>
    /// Event raised when an action is rejected.
    /// Parameters: SignalNotification with SignalType "action-rejected"
    /// </summary>
    public event Func<SignalNotification, Task>? OnActionRejected;

    /// <summary>
    /// Event raised when a workflow is completed.
    /// Parameters: SignalNotification with SignalType "workflow-completed"
    /// </summary>
    public event Func<SignalNotification, Task>? OnWorkflowCompleted;

    /// <summary>
    /// Event raised when encryption progress is updated.
    /// </summary>
    public event Func<EncryptionSignal, Task>? OnEncryptionProgress;

    /// <summary>
    /// Event raised when encryption completes successfully.
    /// </summary>
    public event Func<EncryptionSignal, Task>? OnEncryptionComplete;

    /// <summary>
    /// Event raised when encryption fails.
    /// </summary>
    public event Func<EncryptionSignal, Task>? OnEncryptionFailed;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event Action<ConnectionState>? OnConnectionStateChanged;

    /// <summary>
    /// Event raised when a credential is received by the current user.
    /// </summary>
    public event Action<CredentialNotification>? OnCredentialReceived;

    /// <summary>
    /// Event raised when the status of a credential changes.
    /// </summary>
    public event Action<CredentialNotification>? OnCredentialStatusChanged;

    /// <summary>
    /// Event raised when the server pushes an updated pending credential count.
    /// </summary>
    public event Action<int>? OnPendingCredentialCountUpdated;

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Creates a new ActionsHubConnection.
    /// </summary>
    /// <param name="baseUrl">Base URL of the Blueprint Service (e.g., https://localhost:7000)</param>
    /// <param name="authService">Authentication service for JWT token retrieval</param>
    /// <param name="configurationService">Configuration service for active profile</param>
    /// <param name="logger">Logger for diagnostics</param>
    public ActionsHubConnection(
        string baseUrl,
        IAuthenticationService authService,
        IConfigurationService configurationService,
        ILogger<ActionsHubConnection> logger)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/actionshub";
        _authService = authService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Starts the SignalR connection.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection != null)
        {
            _logger.LogDebug("ActionsHub connection already exists");
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
                        _logger.LogDebug("ActionsHub token provider: profile={Profile}, hasToken={HasToken}",
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
                _logger.LogWarning("ActionsHub reconnecting: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Reconnecting, error?.Message);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async connectionId =>
            {
                _logger.LogInformation("ActionsHub reconnected: {ConnectionId}", connectionId);
                UpdateConnectionState(ConnectionStatus.Connected);

                // Re-subscribe to all previously subscribed wallets
                await ResubscribeAsync();
            };

            _hubConnection.Closed += error =>
            {
                _logger.LogWarning("ActionsHub connection closed: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Disconnected, error?.Message);
                return Task.CompletedTask;
            };

            await _hubConnection.StartAsync(cancellationToken);

            UpdateConnectionState(ConnectionStatus.Connected);
            _logger.LogInformation("ActionsHub connected to {HubUrl}", _hubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to ActionsHub at {HubUrl}", _hubUrl);
            UpdateConnectionState(ConnectionStatus.Disconnected, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Stops the SignalR connection.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection == null)
        {
            return;
        }

        try
        {
            await _hubConnection.StopAsync(cancellationToken);
            UpdateConnectionState(ConnectionStatus.Disconnected);
            _logger.LogInformation("ActionsHub disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ActionsHub connection");
        }
    }

    /// <summary>
    /// Subscribes to notifications for a specific wallet address.
    /// </summary>
    /// <param name="walletAddress">The wallet address to subscribe to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SubscribeToWalletAsync(string walletAddress, CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot subscribe to wallet {WalletAddress}: not connected", walletAddress);
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("SubscribeToWallet", walletAddress, cancellationToken);
            _subscribedWallets.Add(walletAddress);
            _logger.LogDebug("Subscribed to wallet {WalletAddress}", walletAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to wallet {WalletAddress}", walletAddress);
        }
    }

    /// <summary>
    /// Unsubscribes from notifications for a specific wallet address.
    /// </summary>
    /// <param name="walletAddress">The wallet address to unsubscribe from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task UnsubscribeFromWalletAsync(string walletAddress, CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("UnsubscribeFromWallet", walletAddress, cancellationToken);
            _subscribedWallets.Remove(walletAddress);
            _logger.LogDebug("Unsubscribed from wallet {WalletAddress}", walletAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe from wallet {WalletAddress}", walletAddress);
        }
    }

    /// <summary>
    /// Gets the list of currently subscribed wallet addresses.
    /// </summary>
    public IReadOnlySet<string> SubscribedWallets => _subscribedWallets;

    /// <summary>
    /// Registers all event handlers for server-to-client SignalR calls.
    /// </summary>
    private void RegisterEventHandlers()
    {
        if (_hubConnection == null) return;

        // ActionAvailable - thin signal, UI pulls details via REST
        _hubConnection.On<SignalNotification>("ActionAvailable", async signal =>
        {
            _logger.LogDebug(
                "Action available signal: Instance={InstanceId}, CorrelationId={CorrelationId}",
                signal.InstanceId,
                signal.CorrelationId);

            if (OnActionAvailable != null)
            {
                await OnActionAvailable(signal);
            }
        });

        // ActionRejected - thin signal
        _hubConnection.On<SignalNotification>("ActionRejected", async signal =>
        {
            _logger.LogDebug(
                "Action rejected signal: Instance={InstanceId}, CorrelationId={CorrelationId}",
                signal.InstanceId,
                signal.CorrelationId);

            if (OnActionRejected != null)
            {
                await OnActionRejected(signal);
            }
        });

        // WorkflowCompleted - thin signal
        _hubConnection.On<SignalNotification>("WorkflowCompleted", async signal =>
        {
            _logger.LogDebug(
                "Workflow completed signal: Instance={InstanceId}, CorrelationId={CorrelationId}",
                signal.InstanceId,
                signal.CorrelationId);

            if (OnWorkflowCompleted != null)
            {
                await OnWorkflowCompleted(signal);
            }
        });

        // EncryptionProgress - thin signal with progress percentage
        _hubConnection.On<EncryptionSignal>("EncryptionProgress", async signal =>
        {
            _logger.LogDebug(
                "Encryption progress signal: Operation={OperationId}, Progress={Percent}%, Status={Status}",
                signal.OperationId, signal.PercentComplete, signal.Status);

            if (OnEncryptionProgress != null)
            {
                await OnEncryptionProgress(signal);
            }
        });

        // EncryptionComplete - thin signal
        _hubConnection.On<EncryptionSignal>("EncryptionComplete", async signal =>
        {
            _logger.LogDebug(
                "Encryption complete signal: Operation={OperationId}, Status={Status}",
                signal.OperationId, signal.Status);

            if (OnEncryptionComplete != null)
            {
                await OnEncryptionComplete(signal);
            }
        });

        // EncryptionFailed - thin signal
        _hubConnection.On<EncryptionSignal>("EncryptionFailed", async signal =>
        {
            _logger.LogDebug(
                "Encryption failed signal: Operation={OperationId}, Status={Status}",
                signal.OperationId, signal.Status);

            if (OnEncryptionFailed != null)
            {
                await OnEncryptionFailed(signal);
            }
        });

        // CredentialReceived - a new credential has been issued to the current user
        _hubConnection.On<CredentialNotification>("CredentialReceived", notification =>
            OnCredentialReceived?.Invoke(notification));

        // CredentialStatusChanged - an existing credential's status has changed
        _hubConnection.On<CredentialNotification>("CredentialStatusChanged", notification =>
            OnCredentialStatusChanged?.Invoke(notification));

        // PendingCredentialCountUpdated - server-pushed count of pending credentials
        _hubConnection.On<int>("PendingCredentialCountUpdated", count =>
            OnPendingCredentialCountUpdated?.Invoke(count));
    }

    /// <summary>
    /// Re-subscribes to all previously subscribed wallets after reconnection.
    /// </summary>
    private async Task ResubscribeAsync()
    {
        foreach (var walletAddress in _subscribedWallets.ToList())
        {
            try
            {
                await _hubConnection!.InvokeAsync("SubscribeToWallet", walletAddress);
                _logger.LogDebug("Re-subscribed to wallet {WalletAddress}", walletAddress);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-subscribe to wallet {WalletAddress}", walletAddress);
            }
        }
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
