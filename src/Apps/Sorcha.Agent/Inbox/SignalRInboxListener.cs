// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Models;
using Sorcha.ServiceClients.Http.Hub;

namespace Sorcha.Agent.Inbox;

/// <summary>
/// Listens for action notifications via SignalR ActionsHub.
/// </summary>
public class SignalRInboxListener : IInboxListener, IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly string _walletAddress;
    private readonly AgentAuthService _authService;
    private readonly ILogger<SignalRInboxListener> _logger;
    private HubConnection? _connection;
    private readonly Channel<PendingAction> _channel = Channel.CreateUnbounded<PendingAction>();

    /// <summary>
    /// Fires when SignalR reconnects, signalling the composite listener to trigger an immediate poll.
    /// </summary>
    public event Func<Task>? OnReconnected;

    public SignalRInboxListener(
        string hubUrl,
        string walletAddress,
        AgentAuthService authService,
        ILogger<SignalRInboxListener> logger)
    {
        _hubUrl = hubUrl;
        _walletAddress = walletAddress;
        _authService = authService;
        _logger = logger;
    }

    public async IAsyncEnumerable<PendingAction> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _connection = SorchaHubConnectionBuilder.Build(
            _hubUrl,
            _authService.TokenProviderAsync);

        // Thin signal handler — signal contains only type + instanceId.
        // Write a trigger action to the channel so the polling mechanism
        // immediately refreshes the instance to discover pending actions.
        _connection.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (instanceId, actionId, occurredAt, traceId) =>
        {
            try
            {
                const string signalType = "action-available";

                _logger.LogInformation(
                    "Received signal {SignalType} for instance {InstanceId}, triggering poll",
                    signalType, instanceId);

                // Write a trigger with instanceId — the polling mechanism will
                // fetch full details from the instance endpoint
                var trigger = new PendingAction
                {
                    ActionId = $"signal-{instanceId}",
                    ActionName = signalType,
                    InstanceId = instanceId,
                    BlueprintId = string.Empty,
                    RegisterId = string.Empty,
                    TransactionId = string.Empty
                };

                _channel.Writer.TryWrite(trigger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse SignalR signal");
            }
        });

        _connection.Reconnected += _ =>
        {
            _logger.LogInformation("SignalR reconnected, triggering immediate poll");
            OnReconnected?.Invoke();
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync(cancellationToken);
            _logger.LogInformation("SignalR connected to {HubUrl}", _hubUrl);

            // Subscribe to wallet notifications
            await _connection.InvokeAsync("SubscribeToWallet", _walletAddress, cancellationToken);
            _logger.LogInformation("Subscribed to wallet {WalletAddress}", _walletAddress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to SignalR hub, polling will handle discovery");
            yield break;
        }

        await foreach (var action in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return action;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        _channel.Writer.TryComplete();
        GC.SuppressFinalize(this);
    }
}
