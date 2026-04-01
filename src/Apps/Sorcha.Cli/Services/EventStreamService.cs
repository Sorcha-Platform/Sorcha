// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// SignalR client wrapper for streaming real-time events from the Sorcha platform.
/// Connects to register and blueprint event hubs on the API Gateway.
/// </summary>
public class EventStreamService : IAsyncDisposable
{
    private readonly string _gatewayUrl;
    private readonly string? _accessToken;
    private readonly Channel<EventStreamMessage> _channel;
    private HubConnection? _registerHubConnection;
    private HubConnection? _eventsHubConnection;
    private readonly List<IDisposable> _subscriptions = new();
    private int _closedHubCount;

    /// <summary>
    /// Known event types that the service subscribes to.
    /// </summary>
    private static readonly string[] RegisterEventTypes =
    [
        "TransactionConfirmed",
        "DocketSealed",
        "RegisterStatusChanged"
    ];

    /// <summary>
    /// Known blueprint/action event types.
    /// </summary>
    private static readonly string[] BlueprintEventTypes =
    [
        "BlueprintPublished",
        "ActionCompleted"
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
        _eventsHubConnection = BuildHubConnection($"{_gatewayUrl}/hubs/events");

        RegisterEventHandlers(_registerHubConnection, RegisterEventTypes);
        RegisterEventHandlers(_eventsHubConnection, BlueprintEventTypes);

        await _registerHubConnection.StartAsync(cancellationToken);
        await _eventsHubConnection.StartAsync(cancellationToken);
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

        if (_eventsHubConnection is not null)
        {
            await _eventsHubConnection.DisposeAsync();
        }
    }

    /// <summary>
    /// Builds a hub connection with authentication and automatic reconnect.
    /// </summary>
    private HubConnection BuildHubConnection(string url)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                if (!string.IsNullOrEmpty(_accessToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(_accessToken);
                }
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16)
            });

        var connection = builder.Build();

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

            // Only complete the channel when both hubs have closed
            if (Interlocked.Increment(ref _closedHubCount) >= 2)
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
