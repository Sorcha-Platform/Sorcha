// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.Cli.Models;
using Sorcha.ServiceClients.Http.Hub;

namespace Sorcha.Cli.Services;

/// <summary>
/// SignalR client wrapper for streaming real-time events from the Sorcha platform.
/// Connects to register and blueprint hubs on the API Gateway via the shared
/// <see cref="SorchaHubConnectionBuilder"/> (jittered infinite reconnect, JWT auth).
/// </summary>
/// <remarks>
/// Feature 118 Phase 10 polish — rewritten to:
/// <list type="bullet">
///   <item>Use <c>SorchaHubConnectionBuilder</c> instead of a roll-own builder so
///         the CLI shares the platform reconnect-with-jitter policy.</item>
///   <item>Drop the EventsHub connection — that hub is in its parallel-fire
///         deprecation window. Workflow signals come from BlueprintHub now.</item>
///   <item>Connect to RegisterHub and BlueprintHub at their canonical routes
///         (<c>/hubs/register</c> and <c>/hubs/blueprint</c>).</item>
/// </list>
/// </remarks>
public class EventStreamService : IAsyncDisposable
{
    private readonly string _gatewayUrl;
    private readonly string? _accessToken;
    private readonly Channel<EventStreamMessage> _channel;
    private HubConnection? _registerHubConnection;
    private HubConnection? _blueprintHubConnection;
    private readonly List<IDisposable> _subscriptions = new();
    private int _closedHubCount;
    private const int ConnectedHubCount = 2;

    /// <summary>RegisterHub events the CLI surfaces to the watcher.</summary>
    private static readonly string[] RegisterEventTypes =
    [
        "TransactionConfirmed",
        "DocketSealed",
        "RegisterStatusChanged",
        "RegisterCreated",
        "RegisterDeleted",
        "RegisterHeightUpdated",
        "RegisterSyncStateChanged",
        "TransactionReceipt"
    ];

    /// <summary>BlueprintHub events the CLI surfaces to the watcher.</summary>
    /// <remarks>
    /// Replaces the legacy EventsHub event names <c>BlueprintPublished</c> /
    /// <c>ActionCompleted</c>. <c>WorkflowCompleted</c> is the BlueprintHub
    /// equivalent of the latter; <c>BlueprintPublished</c> was admin-only and
    /// is dropped.
    /// </remarks>
    private static readonly string[] BlueprintEventTypes =
    [
        "ActionAvailable",
        "ActionRejected",
        "WorkflowCompleted"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="EventStreamService"/> class.
    /// </summary>
    /// <param name="gatewayUrl">Base URL of the API Gateway (e.g., http://localhost:80).</param>
    /// <param name="accessToken">JWT access token for authentication.</param>
    public EventStreamService(string gatewayUrl, string? accessToken)
    {
        _gatewayUrl = gatewayUrl.TrimEnd('/');
        _accessToken = accessToken;
        _channel = Channel.CreateBounded<EventStreamMessage>(new BoundedChannelOptions(1000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>
    /// Establishes connections to the SignalR hubs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the connection attempt.</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _registerHubConnection = BuildHubConnection($"{_gatewayUrl}/hubs/register");
        _blueprintHubConnection = BuildHubConnection($"{_gatewayUrl}/hubs/blueprint");

        RegisterEventHandlers(_registerHubConnection, RegisterEventTypes);
        RegisterEventHandlers(_blueprintHubConnection, BlueprintEventTypes);

        await _registerHubConnection.StartAsync(cancellationToken);
        await _blueprintHubConnection.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Streams events as an async enumerable. Events are yielded as they arrive from the hubs.
    /// </summary>
    /// <param name="registerId">Optional register ID to filter events for.</param>
    /// <param name="blueprintsOnly">If true, only yield blueprint-related events.</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
    /// <returns>An async stream of <see cref="EventStreamMessage"/> instances.</returns>
    public async IAsyncEnumerable<EventStreamMessage> StreamEventsAsync(
        string? registerId = null,
        bool blueprintsOnly = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            // Filter by register ID if specified
            if (!string.IsNullOrEmpty(registerId) &&
                !string.Equals(message.RegisterId, registerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Filter to blueprint events only if requested
            if (blueprintsOnly && !BlueprintEventTypes.Contains(message.EventType))
            {
                continue;
            }

            yield return message;
        }
    }

    /// <summary>
    /// Disposes hub connections and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();

        _channel.Writer.TryComplete();

        if (_registerHubConnection is not null)
        {
            await _registerHubConnection.DisposeAsync();
        }

        if (_blueprintHubConnection is not null)
        {
            await _blueprintHubConnection.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds a hub connection via <see cref="SorchaHubConnectionBuilder"/> —
    /// shared jittered infinite-reconnect policy + JWT bearer.
    /// </summary>
    private HubConnection BuildHubConnection(string url)
    {
        var connection = SorchaHubConnectionBuilder.Build(
            url,
            tokenProvider: () => Task.FromResult<string?>(_accessToken));

        connection.Reconnecting += error =>
        {
            Console.Error.WriteLine($"Connection lost, reconnecting... ({error?.Message ?? "unknown"})");
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            Console.Error.WriteLine($"Reconnected (connection: {connectionId})");
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            if (error is not null)
            {
                Console.Error.WriteLine($"Connection closed with error: {error.Message}");
            }

            // Only complete the channel when both hubs have closed.
            if (Interlocked.Increment(ref _closedHubCount) >= ConnectedHubCount)
            {
                _channel.Writer.TryComplete();
            }

            return Task.CompletedTask;
        };

        return connection;
    }

    /// <summary>
    /// Registers event handlers on a hub connection for the specified event types.
    /// Each handler writes received events into the shared channel.
    /// </summary>
    private void RegisterEventHandlers(HubConnection connection, string[] eventTypes)
    {
        foreach (var eventType in eventTypes)
        {
            var subscription = connection.On<object>(eventType, data =>
            {
                var message = new EventStreamMessage
                {
                    EventType = eventType,
                    Timestamp = DateTime.UtcNow,
                    Data = data,
                    RegisterId = ExtractRegisterId(data)
                };

                if (!_channel.Writer.TryWrite(message))
                {
                    Console.Error.WriteLine("[WARN] Event dropped — consumer too slow");
                }
            });

            _subscriptions.Add(subscription);
        }
    }

    /// <summary>
    /// Attempts to extract a register ID from the event data.
    /// </summary>
    private static string? ExtractRegisterId(object? data)
    {
        if (data is null)
        {
            return null;
        }

        // Handle JsonElement from SignalR deserialization
        if (data is System.Text.Json.JsonElement jsonElement &&
            jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (jsonElement.TryGetProperty("registerId", out var registerIdProp) ||
                jsonElement.TryGetProperty("RegisterId", out registerIdProp))
            {
                return registerIdProp.GetString();
            }
        }

        return null;
    }
}
