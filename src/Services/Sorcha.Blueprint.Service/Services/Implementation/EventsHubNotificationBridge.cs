// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.ServiceClients.Events;
using Sorcha.ServiceClients.Events.Models;
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

    /// <summary>Initialises a new instance of the <see cref="EventsHubNotificationBridge"/> class.</summary>
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

            // Enrich and persist to Tenant Service activity feed (for pull-back)
            await EnrichAndPersistEventAsync(actionEvent);

            // Send thin signal to user's SignalR group — client pulls detail from activity feed
            var userGroup = $"user:{actionEvent.UserId}";
            var signal = new Hubs.SignalNotification
            {
                SignalType = SignalTypes.InboundAction,
                InstanceId = actionEvent.InstanceId ?? string.Empty,
                CorrelationId = actionEvent.Id
            };
            await _hubContext.Clients.Group(userGroup)
                .SendAsync("InboundActionReceived", signal);

            _logger.LogDebug(
                "Sent inbound-action signal to group {Group} for instance {InstanceId}",
                userGroup, actionEvent.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process notification from {Channel}", PubSubChannel);
        }
    }

    private async Task EnrichAndPersistEventAsync(InboundActionEvent actionEvent)
    {
        // Resolve blueprint name and notification config
        string? blueprintName = null;
        string? actionDescription = null;
        Blueprint.Models.NotificationConfig? notificationConfig = null;
        if (!string.IsNullOrEmpty(actionEvent.BlueprintId))
        {
            try
            {
                var blueprint = await _blueprintStore.GetAsync(actionEvent.BlueprintId);
                if (blueprint is not null)
                {
                    blueprintName = blueprint.Title;
                    // Resolve action description and notification config from the blueprint's actions list
                    var action = blueprint.Actions?.FirstOrDefault(
                        a => a.Id == (int)actionEvent.ActionId);
                    actionDescription = action?.Description ?? action?.Title;
                    notificationConfig = action?.Notification;
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

        // Enrichment: summary, urgency, deadline, groupKey
        var resolvedBlueprintName = blueprintName ?? actionEvent.BlueprintId ?? "Unknown Blueprint";
        var resolvedActionTitle = actionDescription ?? $"Action {actionEvent.ActionId}";
        var defaultSummary = $"{resolvedBlueprintName} — {resolvedActionTitle}";

        // Resolve payload from Instance.AccumulatedData for template rendering.
        // IInstanceStore is resolved via scope factory to avoid captive dependency issues
        // if the store implementation is ever scoped (e.g., MongoDB).
        JsonElement? payload = null;
        if (notificationConfig is not null && !string.IsNullOrEmpty(actionEvent.InstanceId))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var instanceStore = scope.ServiceProvider
                    .GetRequiredService<Storage.IInstanceStore>();
                var instance = await instanceStore.GetAsync(actionEvent.InstanceId);
                if (instance?.AccumulatedData is { Count: > 0 })
                {
                    // Convert AccumulatedData dictionary to JsonElement for template rendering
                    var payloadJson = JsonSerializer.Serialize(instance.AccumulatedData, JsonOptions);
                    payload = JsonSerializer.Deserialize<JsonElement>(payloadJson);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to resolve instance payload for template rendering, using defaults");
            }
        }

        // Use NotificationConfig when available for template-based rendering
        var summary = notificationConfig?.SummaryTemplate is not null
            ? SummaryTemplateRenderer.Render(notificationConfig.SummaryTemplate, payload, defaultSummary)
            : defaultSummary;
        var urgency = UrgencyCalculator.Calculate(notificationConfig, payload);
        var deadline = UrgencyCalculator.ExtractDeadline(notificationConfig, payload);
        // TODO: Resolve GroupBy from NotificationConfig.GroupBy field path + payload (P3 feature)
        string? groupKey = null;

        var notification = new InboundActionNotification
        {
            EventId = actionEvent.Id,
            BlueprintName = resolvedBlueprintName,
            ActionDescription = resolvedActionTitle,
            SenderDisplayName = senderDisplayName,
            NavigationPath = navigationPath,
            TransactionId = actionEvent.TransactionId,
            RegisterId = actionEvent.RegisterId,
            WalletAddress = actionEvent.WalletAddress,
            Timestamp = actionEvent.Timestamp,
            IsRecoveryEvent = actionEvent.IsRecoveryEvent,
            Summary = summary,
            Urgency = urgency,
            Deadline = deadline,
            GroupKey = groupKey
        };

        // Persist as ActivityEvent via Tenant Service and broadcast to activity panel
        await PersistAndBroadcastActivityEventAsync(actionEvent, notification);
    }

    private async Task PersistAndBroadcastActivityEventAsync(InboundActionEvent actionEvent, InboundActionNotification notification)
    {
        var severity = notification.Urgency switch
        {
            "urgent" => "Error",
            "warning" => "Warning",
            _ => "Info"
        };

        var eventId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var userGroup = $"user:{actionEvent.UserId}";

        // 1. Persist to Tenant Service activity log (best-effort)
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var eventClient = scope.ServiceProvider.GetRequiredService<IEventServiceClient>();

            var request = new CreateActivityEventRequest(
                OrganizationId: Guid.TryParse(actionEvent.TenantId, out var tenantGuid) ? tenantGuid : Guid.Empty,
                UserId: Guid.TryParse(actionEvent.UserId, out var userGuid) ? userGuid : Guid.Empty,
                EventType: "PendingAction",
                Severity: severity,
                Title: notification.ActionDescription,
                Message: notification.Summary,
                SourceService: "Blueprint",
                EntityId: actionEvent.InstanceId,
                EntityType: "BlueprintInstance");

            await eventClient.CreateEventAsync(request);

            _logger.LogDebug(
                "Persisted PendingAction activity event for user {UserId}, instance {InstanceId}",
                actionEvent.UserId, actionEvent.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist PendingAction activity event for user {UserId}",
                actionEvent.UserId);
        }

        // 2. Broadcast full event to activity panel via SignalR (real-time update)
        try
        {
            var activityEvent = new
            {
                Id = eventId,
                EventType = "PendingAction",
                Severity = severity,
                Title = notification.ActionDescription ?? "New action available",
                Message = notification.Summary ?? "",
                SourceService = "Blueprint",
                EntityId = actionEvent.InstanceId,
                EntityType = "BlueprintInstance",
                IsRead = false,
                CreatedAt = createdAt,
                UserDisplayName = notification.SenderDisplayName
            };

            await _hubContext.Clients.Group(userGroup)
                .SendAsync("EventReceived", activityEvent);

            _logger.LogDebug(
                "Broadcast EventReceived to group {Group} for instance {InstanceId}",
                userGroup, actionEvent.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast EventReceived for user {UserId}",
                actionEvent.UserId);
        }

        // 3. Broadcast updated unread count
        try
        {
            // Increment is approximate — the client can refresh the exact count on demand.
            // We send -1 as a signal to increment the local counter by 1.
            await _hubContext.Clients.Group(userGroup)
                .SendAsync("UnreadCountUpdated", -1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast UnreadCountUpdated for user {UserId}",
                actionEvent.UserId);
        }
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

    /// <summary>Rendered summary from NotificationConfig template (or default "{BlueprintName} — {ActionTitle}").</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Calculated urgency level: "normal", "warning", or "urgent".</summary>
    public string Urgency { get; init; } = "normal";

    /// <summary>Parsed deadline value, if configured via NotificationConfig.</summary>
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>Resolved grouping key value from NotificationConfig.GroupBy.</summary>
    public string? GroupKey { get; init; }
}
