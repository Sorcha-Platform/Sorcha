// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// PWA-side SignalR connection to the Wallet Service hub at
/// <c>/hubs/wallet</c> (Feature 114, US4).
/// </summary>
/// <remarks>
/// <para>
/// Surfaces only the citizen-wallet-relevant slice of <c>IWalletHubClient</c>:
/// <c>CredentialAvailable</c> (a credential is ready to sync) and
/// <c>DeviceRevoked</c> (this or a sibling device was revoked). Everything else
/// on the hub (transaction lifecycle, encryption pipeline, org-credential events)
/// is wallet-domain noise from the citizen's perspective and is intentionally
/// unsubscribed.
/// </para>
/// <para>
/// Per the Feature 118 thin-signal contract, the events carry opaque IDs only.
/// The wallet always responds by triggering the existing
/// <see cref="ISyncService.SyncAsync"/> pull — push is an optimisation, not the
/// authoritative source of truth. A failure to receive a push (offline, stale
/// reconnect) is recovered automatically by the next pull-on-open.
/// </para>
/// </remarks>
public sealed class CitizenWalletHubConnection : IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly IAccessTokenStore _tokenStore;
    private readonly ILogger<CitizenWalletHubConnection> _logger;
    private HubConnection? _connection;

    /// <summary>A new credential is ready to sync. Carries the opaque credential id.</summary>
    public event Action<string>? OnCredentialAvailable;

    /// <summary>A citizen device was revoked. Carries the opaque device id.</summary>
    public event Action<Guid>? OnDeviceRevoked;

    /// <summary>
    /// A citizen device was enrolled (Feature 128). Carries the opaque
    /// device id. Drives F128 pairing-takeover auto-dismissal when any
    /// device for the citizen pairs successfully (local OR remote).
    /// </summary>
    public event Action<Guid>? OnDeviceEnrolled;

    /// <summary>Initialises a new instance.</summary>
    /// <param name="baseUrl">Base URL of the API gateway, e.g. <c>https://localhost</c>.</param>
    /// <param name="tokenStore">Access-token source for the citizen JWT.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public CitizenWalletHubConnection(
        string baseUrl,
        IAccessTokenStore tokenStore,
        ILogger<CitizenWalletHubConnection> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _hubUrl = baseUrl.TrimEnd('/') + "/hubs/wallet";
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Returns true once <see cref="StartAsync"/> has succeeded.</summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Connect to the hub. No-op if already connected. Failures are logged and
    /// swallowed — a missing push is a UX optimisation regression, never a
    /// correctness issue.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connection is not null) return;

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var record = await _tokenStore.GetAsync();
                    return record?.AccessToken;
                };
            })
            .WithAutomaticReconnect(new Sorcha.ServiceClients.Http.Hub.SorchaHubConnectionBuilder.JitteredRetryPolicy(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)))
            .Build();

        _connection.On<string>("CredentialAvailable", credentialId =>
        {
            _logger.LogDebug("CitizenWalletHub CredentialAvailable: {CredentialId}", credentialId);
            OnCredentialAvailable?.Invoke(credentialId);
        });

        _connection.On<Guid>("DeviceRevoked", deviceId =>
        {
            _logger.LogDebug("CitizenWalletHub DeviceRevoked: {DeviceId}", deviceId);
            OnDeviceRevoked?.Invoke(deviceId);
        });

        _connection.On<Guid>("DeviceEnrolled", deviceId =>
        {
            _logger.LogDebug("CitizenWalletHub DeviceEnrolled: {DeviceId}", deviceId);
            OnDeviceEnrolled?.Invoke(deviceId);
        });

        _connection.Reconnecting += error =>
        {
            _logger.LogWarning("CitizenWalletHub reconnecting: {Error}", error?.Message);
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("CitizenWalletHub reconnected: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        _connection.Closed += error =>
        {
            _logger.LogWarning("CitizenWalletHub closed: {Error}", error?.Message);
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync(ct);
            _logger.LogInformation("CitizenWalletHub connected: {Url}", _hubUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CitizenWalletHub failed to connect to {Url}", _hubUrl);
            // Don't propagate — the wallet always reconciles via the pull-on-open path.
        }
    }

    /// <summary>Stops the connection. Safe to call when never started.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_connection is null) return;
        try
        {
            await _connection.StopAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CitizenWalletHub stop failed (ignored)");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        GC.SuppressFinalize(this);
    }
}
