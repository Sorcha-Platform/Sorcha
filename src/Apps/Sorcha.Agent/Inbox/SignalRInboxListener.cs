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

        _connection.On<object>("ActionAvailable", notification =>
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(notification);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Resolve instance ID (present in both notification shapes)
                var instanceId = root.TryGetProperty("InstanceId", out var instProp)
                    ? instProp.GetString() ?? ""
                    : root.TryGetProperty("instanceId", out var inst2) ? inst2.GetString() ?? "" : "";

                // Resolve action index — new shape has ActionId (int), old shape has actionId (string)
                uint actionIndex = 0;
                if (root.TryGetProperty("ActionId", out var aidInt) && aidInt.ValueKind == System.Text.Json.JsonValueKind.Number)
                    actionIndex = aidInt.GetUInt32();
                else if (root.TryGetProperty("actionId", out var aidStr) && aidStr.ValueKind == System.Text.Json.JsonValueKind.Number)
                    actionIndex = aidStr.GetUInt32();

                // Resolve action title
                var actionTitle = root.TryGetProperty("ActionTitle", out var titleProp)
                    ? titleProp.GetString() ?? ""
                    : root.TryGetProperty("actionTitle", out var title2) ? title2.GetString() ?? "" : "";

                var action = new PendingAction
                {
                    ActionId = $"{instanceId}-{actionIndex}",
                    ActionName = actionTitle,
                    ActionIndex = actionIndex,
                    BlueprintId = root.TryGetProperty("blueprintId", out var bp) ? bp.GetString() ?? "" : "",
                    InstanceId = instanceId,
                    RegisterId = root.TryGetProperty("registerAddress", out var reg) ? reg.GetString() ?? "" : "",
                    TransactionId = root.TryGetProperty("transactionHash", out var tx) ? tx.GetString() ?? "" : ""
                };

                _logger.LogInformation("SignalR: ActionAvailable {ActionName} (instance {InstanceId}, action {ActionIndex})",
                    action.ActionName, instanceId, actionIndex);

                _channel.Writer.TryWrite(action);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse SignalR notification");
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
