// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Http.Hub;
using Sorcha.UI.Core.Models.Admin;
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
    private readonly Func<CancellationToken, Task>? _pollFallback;
    private readonly string _hubUrl;
    private HubConnection? _hubConnection;
    private HubConnectionWithFallback? _fallbackWrapper;
    private ConnectionState _connectionState = new();

    /// <summary>Citizen device was revoked (admin- or self-initiated).</summary>
    public event Action<Guid>? OnDeviceRevoked;

    /// <summary>A new credential is available for the citizen's wallet to sync.</summary>
    public event Action<string>? OnCredentialAvailable;

    /// <summary>Encryption operation progressed. Detail via <c>GET /api/operations/{operationId}</c>.</summary>
    public event Func<EncryptionSignal, Task>? OnEncryptionProgress;

    /// <summary>Encryption operation completed successfully. Detail via <c>GET /api/operations/{operationId}</c>.</summary>
    public event Func<EncryptionSignal, Task>? OnEncryptionComplete;

    /// <summary>Encryption operation failed. Detail via <c>GET /api/operations/{operationId}</c>.</summary>
    public event Func<EncryptionSignal, Task>? OnEncryptionFailed;

    /// <summary>
    /// A new inbound transaction arrived for the wallet (post peer-replication).
    /// Parameters: walletAddress, transactionId. Detail via
    /// <c>GET /api/wallets/{walletAddress}/transactions/{transactionId}</c>.
    /// </summary>
    public event Action<string, string>? OnTransactionReceived;

    /// <summary>
    /// A transaction sealed by the validator. Tick state advances to confirmed.
    /// Parameters: walletAddress, transactionId.
    /// </summary>
    public event Action<string, string>? OnTransactionConfirmed;

    /// <summary>
    /// A transaction receipt was generated. Tick state advances to receipted.
    /// Parameters: walletAddress, transactionId, receiptId.
    /// </summary>
    public event Action<string, string, string>? OnTransactionReceipted;

    /// <summary>
    /// A new org credential was issued to the wallet. Parameters: walletAddress, credentialId.
    /// Detail via <c>GET /api/wallets/{walletAddress}/credentials/{credentialId}</c>.
    /// </summary>
    public event Action<string, string>? OnCredentialReceived;

    /// <summary>
    /// An org credential's status changed. Parameters: walletAddress, credentialId.
    /// </summary>
    public event Action<string, string>? OnCredentialStatusChanged;

    /// <summary>
    /// The pending-credential count for the wallet changed. Parameters:
    /// walletAddress, pendingCount.
    /// </summary>
    public event Action<string, int>? OnPendingCredentialCountUpdated;

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
        ILogger<WalletHubConnection> logger,
        Func<CancellationToken, Task>? pollFallback = null)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/wallet";
        _authService = authService;
        _configurationService = configurationService;
        _logger = logger;
        _pollFallback = pollFallback;
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
                .WithAutomaticReconnect(new SorchaHubConnectionBuilder.JitteredRetryPolicy(
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)))
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

            // Feature 118 T101 — REST polling fallback engages after 90s of
            // failed reconnect. Refresher delegate (passed via ctor) typically
            // invokes /api/wallets/{addr} or /api/v1/wallet/sync and re-raises
            // the relevant events. Default null = state observable only.
            _fallbackWrapper = new HubConnectionWithFallback(_hubConnection, _pollFallback);

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

        // Feature 118 — encryption pipeline migrated off BlueprintHub. Wire shape
        // is (operationId, occurredAt, traceId); UI consumers fetch percent /
        // stage / recipient state via /api/operations/{operationId}.
        _hubConnection.On<string, DateTimeOffset, string>("EncryptionProgress",
            async (operationId, occurredAt, traceId) =>
            {
                _logger.LogDebug("WalletHub EncryptionProgress: {OperationId}", operationId);
                if (OnEncryptionProgress is not null)
                {
                    await OnEncryptionProgress(new EncryptionSignal
                    {
                        OperationId = operationId,
                        PercentComplete = 0,
                        Status = "encrypting",
                        Timestamp = occurredAt,
                    });
                }
            });

        _hubConnection.On<string, DateTimeOffset, string>("EncryptionComplete",
            async (operationId, occurredAt, traceId) =>
            {
                _logger.LogDebug("WalletHub EncryptionComplete: {OperationId}", operationId);
                if (OnEncryptionComplete is not null)
                {
                    await OnEncryptionComplete(new EncryptionSignal
                    {
                        OperationId = operationId,
                        PercentComplete = 100,
                        Status = "complete",
                        Timestamp = occurredAt,
                    });
                }
            });

        _hubConnection.On<string, DateTimeOffset, string>("EncryptionFailed",
            async (operationId, occurredAt, traceId) =>
            {
                _logger.LogDebug("WalletHub EncryptionFailed: {OperationId}", operationId);
                if (OnEncryptionFailed is not null)
                {
                    await OnEncryptionFailed(new EncryptionSignal
                    {
                        OperationId = operationId,
                        PercentComplete = 0,
                        Status = "failed",
                        Timestamp = occurredAt,
                    });
                }
            });

        // Feature 118 — transaction lifecycle events forward-declared on
        // IWalletHubClient (T045). No active server emit yet; handlers are
        // wired so consumer pages don't need a follow-up subscribe.
        _hubConnection.On<string, string, DateTimeOffset, string>("TransactionReceived",
            (walletAddress, transactionId, _, _) =>
            {
                _logger.LogDebug("WalletHub TransactionReceived: {Wallet}/{Tx}", walletAddress, transactionId);
                OnTransactionReceived?.Invoke(walletAddress, transactionId);
            });

        _hubConnection.On<string, string, DateTimeOffset, string>("TransactionConfirmed",
            (walletAddress, transactionId, _, _) =>
            {
                _logger.LogDebug("WalletHub TransactionConfirmed: {Wallet}/{Tx}", walletAddress, transactionId);
                OnTransactionConfirmed?.Invoke(walletAddress, transactionId);
            });

        _hubConnection.On<string, string, string, DateTimeOffset, string>("TransactionReceipted",
            (walletAddress, transactionId, receiptId, _, _) =>
            {
                _logger.LogDebug("WalletHub TransactionReceipted: {Wallet}/{Tx}/{Receipt}",
                    walletAddress, transactionId, receiptId);
                OnTransactionReceipted?.Invoke(walletAddress, transactionId, receiptId);
            });

        _hubConnection.On<string, string, DateTimeOffset, string>("CredentialReceived",
            (walletAddress, credentialId, _, _) =>
            {
                _logger.LogDebug("WalletHub CredentialReceived: {Wallet}/{Cred}", walletAddress, credentialId);
                OnCredentialReceived?.Invoke(walletAddress, credentialId);
            });

        _hubConnection.On<string, string, DateTimeOffset, string>("CredentialStatusChanged",
            (walletAddress, credentialId, _, _) =>
            {
                _logger.LogDebug("WalletHub CredentialStatusChanged: {Wallet}/{Cred}", walletAddress, credentialId);
                OnCredentialStatusChanged?.Invoke(walletAddress, credentialId);
            });

        _hubConnection.On<string, int, DateTimeOffset, string>("PendingCredentialCountUpdated",
            (walletAddress, pendingCount, _, _) =>
            {
                _logger.LogDebug("WalletHub PendingCredentialCountUpdated: {Wallet} count={Count}",
                    walletAddress, pendingCount);
                OnPendingCredentialCountUpdated?.Invoke(walletAddress, pendingCount);
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
        if (_fallbackWrapper is not null)
        {
            await _fallbackWrapper.DisposeAsync();
            _fallbackWrapper = null;
        }
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        GC.SuppressFinalize(this);
    }
}
