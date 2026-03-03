// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.ServiceClients.Models;
using Sorcha.ServiceClients.Participant;
using StackExchange.Redis;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// IHostedService that bridges Redis pub/sub notifications from Wallet Service
/// to SignalR EventsHub for real-time user delivery.
/// Subscribes to the "wallet:notifications" Redis channel, enriches each event
/// with blueprint name, action description, sender display name, and navigation path,
/// then pushes via IHubContext&lt;EventsHub&gt; to the target user's SignalR group.
/// </summary>
public sealed class EventsHubNotificationBridge : IHostedService, IDisposable
{
    private const string PubSubChannel = "wallet:notifications";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly IBlueprintStore _blueprintStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventsHubNotificationBridge> _logger;
    private ISubscriber? _subscriber;

    public EventsHubNotificationBridge(
        IConnectionMultiplexer redis,
        IHubContext<EventsHub> hubContext,
        IBlueprintStore blueprintStore,
        IServiceScopeFactory scopeFactory,
        ILogger<EventsHubNotificationBridge> logger)
    {
        _redis = redis;
        _hubContext = hubContext;
        _blueprintStore = blueprintStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventsHubNotificationBridge starting — subscribing to {Channel}", PubSubChannel);

        _subscriber = _redis.GetSubscriber();
        await _subscriber.SubscribeAsync(
            RedisChannel.Literal(PubSubChannel),
            async (_, message) => await HandleNotificationAsync(message));

        _logger.LogInformation("EventsHubNotificationBridge started — listening on {Channel}", PubSubChannel);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventsHubNotificationBridge stopping");

        if (_subscriber is not null)
        {
            await _subscriber.UnsubscribeAsync(RedisChannel.Literal(PubSubChannel));
        }

        _logger.LogInformation("EventsHubNotificationBridge stopped");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscriber = null;
    }

    private async Task HandleNotificationAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty)
            return;

        try
        {
            var json = message.ToString();

            // Discriminate between real-time and digest notifications on the shared channel.
            // DigestNotification payloads contain "blueprintGroups"; individual events do not.
            if (json.Contains("\"blueprintGroups\"", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Received digest notification on {Channel}, forwarding as digest event", PubSubChannel);

                using var doc = JsonDocument.Parse(json);
                var userId = doc.RootElement.TryGetProperty("userId", out var uid) ? uid.GetString() : null;
                if (userId is not null)
                {
                    var groupName = $"user:{userId}";
                    await _hubContext.Clients.Group(groupName)
                        .SendAsync("DigestNotificationReceived", json);
                }

                return;
            }

            var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(json, JsonOptions);

            if (actionEvent is null)
            {
                _logger.LogWarning("Received null event from {Channel}", PubSubChannel);
                return;
            }

            // Enrich with blueprint name, action description, sender display name
            var enrichedPayload = await EnrichEventAsync(actionEvent);

            // Push to user's SignalR group
            var userGroup = $"user:{actionEvent.UserId}";
            await _hubContext.Clients.Group(userGroup)
                .SendAsync("InboundActionReceived", enrichedPayload);

            _logger.LogDebug(
                "Pushed InboundActionReceived to group {Group} for tx {TxId}",
                userGroup, actionEvent.TransactionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process notification from {Channel}", PubSubChannel);
        }
    }

    private async Task<InboundActionNotification> EnrichEventAsync(InboundActionEvent actionEvent)
    {
        // Resolve blueprint name
        string? blueprintName = null;
        string? actionDescription = null;
        if (!string.IsNullOrEmpty(actionEvent.BlueprintId))
        {
            try
            {
                var blueprint = await _blueprintStore.GetAsync(actionEvent.BlueprintId);
                if (blueprint is not null)
                {
                    blueprintName = blueprint.Title;
                    // Resolve action description from the blueprint's actions list
                    var action = blueprint.Actions?.FirstOrDefault(
                        a => a.Id == (int)actionEvent.ActionId);
                    actionDescription = action?.Description ?? action?.Title;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to resolve blueprint {BlueprintId} for enrichment",
                    actionEvent.BlueprintId);
            }
        }

        // Resolve sender display name (fall back to raw address)
        string senderDisplayName = actionEvent.SenderAddress ?? "Unknown";
        if (!string.IsNullOrEmpty(actionEvent.SenderAddress))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var participantClient = scope.ServiceProvider
                    .GetRequiredService<IParticipantServiceClient>();
                var participant = await participantClient.GetByWalletAddressAsync(
                    actionEvent.SenderAddress);
                if (participant is not null)
                {
                    senderDisplayName = participant.DisplayName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to resolve sender participant for address {Address}, using raw address",
                    actionEvent.SenderAddress);
            }
        }

        // Construct navigation path
        var navigationPath = !string.IsNullOrEmpty(actionEvent.BlueprintId) && !string.IsNullOrEmpty(actionEvent.InstanceId)
            ? $"/blueprints/{actionEvent.BlueprintId}/instances/{actionEvent.InstanceId}/actions/{actionEvent.ActionId}"
            : null;

        return new InboundActionNotification
        {
            EventId = actionEvent.Id,
            BlueprintName = blueprintName ?? actionEvent.BlueprintId ?? "Unknown Blueprint",
            ActionDescription = actionDescription ?? $"Action {actionEvent.ActionId}",
            SenderDisplayName = senderDisplayName,
            NavigationPath = navigationPath,
            TransactionId = actionEvent.TransactionId,
            RegisterId = actionEvent.RegisterId,
            WalletAddress = actionEvent.WalletAddress,
            Timestamp = actionEvent.Timestamp,
            IsRecoveryEvent = actionEvent.IsRecoveryEvent
        };
    }
}

/// <summary>
/// Enriched inbound action notification payload pushed to clients via SignalR.
/// </summary>
public record InboundActionNotification
{
    /// <summary>Unique event identifier.</summary>
    public Guid EventId { get; init; }

    /// <summary>Resolved blueprint display name.</summary>
    public required string BlueprintName { get; init; }

    /// <summary>Resolved action description or title.</summary>
    public required string ActionDescription { get; init; }

    /// <summary>Sender display name (resolved from participant registry, or raw address).</summary>
    public required string SenderDisplayName { get; init; }

    /// <summary>Navigation path for the UI to route to the relevant action.</summary>
    public string? NavigationPath { get; init; }

    /// <summary>64-char hex SHA-256 transaction hash.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Register the transaction belongs to.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Recipient wallet address.</summary>
    public required string WalletAddress { get; init; }

    /// <summary>When the event was detected.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Whether this was detected during recovery mode.</summary>
    public bool IsRecoveryEvent { get; init; }
}
