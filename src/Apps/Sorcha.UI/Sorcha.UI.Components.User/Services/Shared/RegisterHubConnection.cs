// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Http.Hub;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Manages SignalR connection to the Register Hub for real-time updates.
/// </summary>
public class RegisterHubConnection : IAsyncDisposable
{
    private readonly ILogger<RegisterHubConnection> _logger;
    private readonly string _hubUrl;
    private readonly Func<Task<string?>>? _accessTokenProvider;
    private readonly Func<CancellationToken, Task>? _pollFallback;
    private HubConnection? _hubConnection;
    private HubConnectionWithFallback? _fallbackWrapper;
    private ConnectionState _connectionState = new();
    private readonly HashSet<string> _subscribedRegisters = [];
    private CancellationTokenSource? _retryCts;
    private bool _disposed;

    /// <summary>
    /// Event raised when a new transaction is confirmed.
    /// </summary>
    public event Func<string, string, Task>? OnTransactionConfirmed;

    /// <summary>
    /// Event raised when a register is created.
    /// </summary>
    public event Func<string, string, Task>? OnRegisterCreated;

    /// <summary>
    /// Event raised when a register is deleted.
    /// </summary>
    public event Func<string, Task>? OnRegisterDeleted;

    /// <summary>
    /// Event raised when a docket is sealed (new block).
    /// </summary>
    public event Func<string, ulong, string, Task>? OnDocketSealed;

    /// <summary>
    /// Event raised when register height is updated.
    /// </summary>
    public event Func<string, uint, Task>? OnRegisterHeightUpdated;

    /// <summary>
    /// Event raised when register status changes.
    /// </summary>
    public event Func<string, string, Task>? OnRegisterStatusChanged;

    /// <summary>
    /// Event raised when register sync state changes.
    /// </summary>
    public event Func<string, string, Task>? OnRegisterSyncStateChanged;

    /// <summary>
    /// Event raised when a transaction receipt is generated (double blue tick).
    /// Parameters: transactionId, registerId, docketNumber, receiptId, sealedAt
    /// </summary>
    public event Func<string, string, long, string, DateTimeOffset, Task>? OnTransactionReceipted;

    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    public event Action<ConnectionState>? OnConnectionStateChanged;

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Initialises the connection without an access-token provider. Compatible
    /// with the pre-Feature-118 wire shape — used by tests and any caller that
    /// has not yet been migrated to authenticated hubs.
    /// </summary>
    public RegisterHubConnection(string baseUrl, ILogger<RegisterHubConnection> logger)
        : this(baseUrl, accessTokenProvider: null, logger, pollFallback: null)
    {
    }

    /// <summary>
    /// Initialises the connection with an access-token provider. The token
    /// rides on the SignalR negotiate as <c>?access_token=</c> and on the
    /// websocket upgrade as a query string. Required for the upcoming
    /// RegisterHub <c>[Authorize]</c> cutover (T080); permissive on the
    /// server today so this change is forward-compatible (T089).
    /// </summary>
    public RegisterHubConnection(
        string baseUrl,
        Func<Task<string?>>? accessTokenProvider,
        ILogger<RegisterHubConnection> logger,
        Func<CancellationToken, Task>? pollFallback = null)
    {
        _hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/register";
        _accessTokenProvider = accessTokenProvider;
        _logger = logger;
        _pollFallback = pollFallback;
    }

    /// <summary>
    /// Starts the SignalR connection. If a dead connection exists, it is disposed first.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_hubConnection is not null)
        {
            // Already connected and healthy — nothing to do
            if (_hubConnection.State == HubConnectionState.Connected)
                return;

            // Connection exists but is dead/disconnected — tear it down
            await DisposeHubConnectionAsync();
        }

        CancelBackgroundRetry();
        UpdateConnectionState(ConnectionStatus.Connecting);

        try
        {
            _hubConnection = new HubConnectionBuilder()
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
                })
                .Build();

            // Register event handlers
            _hubConnection.On<string, string>("TransactionConfirmed", async (registerId, txId) =>
            {
                _logger.LogDebug("Transaction confirmed: {TxId} in register {RegisterId}", txId, registerId);
                if (OnTransactionConfirmed != null)
                {
                    await OnTransactionConfirmed(registerId, txId);
                }
            });

            _hubConnection.On<string, string>("RegisterCreated", async (registerId, name) =>
            {
                _logger.LogDebug("Register created: {RegisterId} ({Name})", registerId, name);
                if (OnRegisterCreated != null)
                {
                    await OnRegisterCreated(registerId, name);
                }
            });

            _hubConnection.On<string>("RegisterDeleted", async (registerId) =>
            {
                _logger.LogDebug("Register deleted: {RegisterId}", registerId);
                if (OnRegisterDeleted != null)
                {
                    await OnRegisterDeleted(registerId);
                }
            });

            _hubConnection.On<string, ulong, string>("DocketSealed", async (registerId, docketId, hash) =>
            {
                _logger.LogDebug("Docket sealed: {DocketId} in register {RegisterId}", docketId, registerId);
                if (OnDocketSealed != null)
                {
                    await OnDocketSealed(registerId, docketId, hash);
                }
            });

            _hubConnection.On<string, string, long, string, DateTimeOffset>("TransactionReceipt", async (txId, registerId, docketNumber, receiptId, sealedAt) =>
            {
                _logger.LogDebug("Transaction receipted: {TxId} in register {RegisterId} (receipt {ReceiptId})", txId, registerId, receiptId);
                if (OnTransactionReceipted != null)
                {
                    await OnTransactionReceipted(txId, registerId, docketNumber, receiptId, sealedAt);
                }
            });

            _hubConnection.On<string, uint>("RegisterHeightUpdated", async (registerId, newHeight) =>
            {
                _logger.LogDebug("Register height updated: {RegisterId} -> {Height}", registerId, newHeight);
                if (OnRegisterHeightUpdated != null)
                {
                    await OnRegisterHeightUpdated(registerId, newHeight);
                }
            });

            _hubConnection.On<string, string>("RegisterStatusChanged", async (registerId, status) =>
            {
                _logger.LogDebug("Register status changed: {RegisterId} -> {Status}", registerId, status);
                if (OnRegisterStatusChanged != null)
                {
                    await OnRegisterStatusChanged(registerId, status);
                }
            });

            _hubConnection.On<string, string>("RegisterSyncStateChanged", async (registerId, syncState) =>
            {
                _logger.LogDebug("Register sync state changed: {RegisterId} -> {SyncState}", registerId, syncState);
                if (OnRegisterSyncStateChanged != null)
                {
                    await OnRegisterSyncStateChanged(registerId, syncState);
                }
            });

            // Handle connection lifecycle
            _hubConnection.Reconnecting += error =>
            {
                _logger.LogWarning("SignalR reconnecting: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Reconnecting, error?.Message);
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async connectionId =>
            {
                _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
                UpdateConnectionState(ConnectionStatus.Connected);

                // Re-subscribe to registers and tenant
                await ResubscribeAsync();
            };

            _hubConnection.Closed += async error =>
            {
                _logger.LogWarning("SignalR connection closed: {Error}", error?.Message);
                UpdateConnectionState(ConnectionStatus.Disconnected, error?.Message);

                // Start background retry — the built-in reconnect has been exhausted
                _ = BackgroundRetryAsync();
            };

            // Feature 118 T104 — REST polling fallback engages after 90s of
            // failed reconnect. Refresher delegate (passed via ctor) typically
            // calls /api/registers/{id} per subscribed register and re-raises
            // the relevant events. Default null = state observable only.
            _fallbackWrapper = new HubConnectionWithFallback(_hubConnection, _pollFallback);

            await _hubConnection.StartAsync(cancellationToken);

            UpdateConnectionState(ConnectionStatus.Connected);
            _logger.LogInformation("SignalR connected to {HubUrl}", _hubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub at {HubUrl}", _hubUrl);
            UpdateConnectionState(ConnectionStatus.Disconnected, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Stops the SignalR connection and cancels any background retry.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancelBackgroundRetry();

        if (_hubConnection == null)
        {
            return;
        }

        try
        {
            await _hubConnection.StopAsync(cancellationToken);
            _logger.LogInformation("SignalR disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping SignalR connection");
        }
        finally
        {
            await DisposeHubConnectionAsync();
            UpdateConnectionState(ConnectionStatus.Disconnected);
        }
    }

    /// <summary>
    /// Tears down the current connection and establishes a fresh one,
    /// re-subscribing to all previously active registers.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Manual reconnect requested");
        CancelBackgroundRetry();

        await DisposeHubConnectionAsync();
        await StartAsync(cancellationToken);

        // Re-subscribe after fresh connection
        await ResubscribeAsync();
    }

    /// <summary>
    /// Subscribes to updates for a specific register.
    /// </summary>
    public async Task SubscribeToRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            _logger.LogWarning("Cannot subscribe to register {RegisterId}: not connected", registerId);
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("SubscribeToRegister", registerId, cancellationToken);
            _subscribedRegisters.Add(registerId);
            _logger.LogDebug("Subscribed to register {RegisterId}", registerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to register {RegisterId}", registerId);
        }
    }

    /// <summary>
    /// Unsubscribes from updates for a specific register.
    /// </summary>
    public async Task UnsubscribeFromRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await _hubConnection.InvokeAsync("UnsubscribeFromRegister", registerId, cancellationToken);
            _subscribedRegisters.Remove(registerId);
            _logger.LogDebug("Unsubscribed from register {RegisterId}", registerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe from register {RegisterId}", registerId);
        }
    }

    private async Task ResubscribeAsync()
    {
        // Re-subscribe to all previously subscribed registers
        foreach (var registerId in _subscribedRegisters.ToList())
        {
            try
            {
                await _hubConnection!.InvokeAsync("SubscribeToRegister", registerId);
                _logger.LogDebug("Re-subscribed to register {RegisterId}", registerId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to re-subscribe to register {RegisterId}", registerId);
            }
        }
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

    /// <summary>
    /// Background retry loop that fires after built-in WithAutomaticReconnect is exhausted.
    /// Retries every 60s up to 10 times, then gives up.
    /// </summary>
    private async Task BackgroundRetryAsync()
    {
        CancelBackgroundRetry();
        _retryCts = new CancellationTokenSource();
        var token = _retryCts.Token;

        const int maxAttempts = 10;
        var delay = TimeSpan.FromSeconds(60);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (TaskCanceledException)
            {
                return; // Cancelled — manual reconnect or dispose took over
            }

            if (_disposed || token.IsCancellationRequested)
                return;

            _logger.LogInformation("Background retry attempt {Attempt}/{Max}", attempt, maxAttempts);

            try
            {
                await DisposeHubConnectionAsync();
                await StartAsync(token);
                await ResubscribeAsync();
                _logger.LogInformation("Background retry succeeded on attempt {Attempt}", attempt);
                return; // Success
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background retry attempt {Attempt}/{Max} failed", attempt, maxAttempts);
            }
        }

        _logger.LogError("Background retry exhausted after {Max} attempts", maxAttempts);
    }

    private void CancelBackgroundRetry()
    {
        if (_retryCts is not null)
        {
            _retryCts.Cancel();
            _retryCts.Dispose();
            _retryCts = null;
        }
    }

    private async Task DisposeHubConnectionAsync()
    {
        if (_fallbackWrapper is not null)
        {
            try { await _fallbackWrapper.DisposeAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error disposing fallback wrapper"); }
            _fallbackWrapper = null;
        }
        if (_hubConnection is not null)
        {
            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing hub connection");
            }

            _hubConnection = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CancelBackgroundRetry();

        await DisposeHubConnectionAsync();

        GC.SuppressFinalize(this);
    }
}
