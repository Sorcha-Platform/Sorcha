// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Sorcha.ServiceDefaults.Hubs;
using Sorcha.Wallet.Service.Hubs;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services;

/// <summary>
/// Subscribes to the cross-service <see cref="EncryptionEventEnvelope.ChannelName"/>
/// Redis channel published by Blueprint Service's NotificationService and
/// re-emits each event on the typed <see cref="WalletHub"/> client. Closes
/// the FR-006 boundary that prevented direct cross-service hub emission.
/// </summary>
public sealed class EncryptionEventBridge : IHostedService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<WalletHub, IWalletHubClient> _hubContext;
    private readonly ILogger<EncryptionEventBridge> _logger;
    private ISubscriber? _subscriber;

    /// <summary>Initialises a new instance of the <see cref="EncryptionEventBridge"/> class.</summary>
    public EncryptionEventBridge(
        IConnectionMultiplexer redis,
        IHubContext<WalletHub, IWalletHubClient> hubContext,
        ILogger<EncryptionEventBridge> logger)
    {
        _redis = redis;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "EncryptionEventBridge starting — subscribing to {Channel}",
            EncryptionEventEnvelope.ChannelName);

        _subscriber = _redis.GetSubscriber();
        await _subscriber.SubscribeAsync(
            RedisChannel.Literal(EncryptionEventEnvelope.ChannelName),
            async (_, message) => await HandleAsync(message));

        _logger.LogInformation("EncryptionEventBridge started");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null)
        {
            await _subscriber.UnsubscribeAsync(
                RedisChannel.Literal(EncryptionEventEnvelope.ChannelName));
        }

        _logger.LogInformation("EncryptionEventBridge stopped");
    }

    /// <inheritdoc />
    public void Dispose() => _subscriber = null;

    private async Task HandleAsync(RedisValue message)
    {
        if (message.IsNullOrEmpty)
            return;

        try
        {
            var envelope = JsonSerializer.Deserialize<EncryptionEventEnvelope>(
                message.ToString()!, JsonOptions);

            if (envelope is null || string.IsNullOrEmpty(envelope.WalletAddress) ||
                string.IsNullOrEmpty(envelope.OperationId))
            {
                _logger.LogWarning("Discarded malformed encryption event envelope: {Body}", message);
                return;
            }

            var group = WalletHubGroups.Wallet(envelope.WalletAddress);
            var client = _hubContext.Clients.Group(group);

            switch (envelope.Kind)
            {
                case EncryptionEventEnvelope.KindProgress:
                    await client.EncryptionProgress(envelope.OperationId, envelope.OccurredAt, envelope.TraceId);
                    break;
                case EncryptionEventEnvelope.KindComplete:
                    await client.EncryptionComplete(envelope.OperationId, envelope.OccurredAt, envelope.TraceId);
                    break;
                case EncryptionEventEnvelope.KindFailed:
                    await client.EncryptionFailed(envelope.OperationId, envelope.OccurredAt, envelope.TraceId);
                    break;
                default:
                    _logger.LogWarning("Unknown encryption event kind {Kind}", envelope.Kind);
                    break;
            }

            _logger.LogDebug(
                "Re-emitted encryption {Kind} on WalletHub for wallet {Wallet}, operation {OperationId}",
                envelope.Kind, envelope.WalletAddress, envelope.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to handle encryption event from {Channel}",
                EncryptionEventEnvelope.ChannelName);
        }
    }
}
