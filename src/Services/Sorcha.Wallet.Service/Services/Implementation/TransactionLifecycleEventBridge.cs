// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Background service that subscribes to Redis pub/sub events for docket confirmations
/// and receipt generation, updating wallet transaction lifecycle records.
///
/// Listens for:
/// - "docket:confirmed" → marks transactions as sealed (blue tick)
/// - "receipt:generated" → marks transactions as receipted (double blue tick)
/// </summary>
public class TransactionLifecycleEventBridge : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<TransactionLifecycleEventBridge> _logger;

    public TransactionLifecycleEventBridge(
        IServiceScopeFactory scopeFactory,
        ILogger<TransactionLifecycleEventBridge> logger,
        IConnectionMultiplexer? redis = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redis = redis;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_redis == null)
        {
            _logger.LogWarning(
                "Redis not available — transaction lifecycle events will not be processed. " +
                "Tick states will update on next page load via API query.");
            return;
        }

        _logger.LogInformation("Transaction lifecycle event bridge starting");

        try
        {
            var subscriber = _redis.GetSubscriber();

            // Subscribe to docket:confirmed events
            await subscriber.SubscribeAsync(
                RedisChannel.Literal("docket:confirmed"),
                async (_, message) =>
                {
                    try
                    {
                        await HandleDocketConfirmedAsync(message!, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error handling docket:confirmed event");
                    }
                });

            // Subscribe to receipt:generated events
            await subscriber.SubscribeAsync(
                RedisChannel.Literal("receipt:generated"),
                async (_, message) =>
                {
                    try
                    {
                        await HandleReceiptGeneratedAsync(message!, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error handling receipt:generated event");
                    }
                });

            _logger.LogInformation("Transaction lifecycle event bridge subscribed to Redis channels");

            // Keep alive until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Transaction lifecycle event bridge stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction lifecycle event bridge failed");
        }
    }

    private async Task HandleDocketConfirmedAsync(string message, CancellationToken ct)
    {
        // Parse the event — expected format from RegisterEventBridgeService:
        // JSON with RegisterId, DocketId (number), TransactionIds[], Hash
        try
        {
            var evt = System.Text.Json.JsonSerializer.Deserialize<DocketConfirmedEvent>(message,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (evt?.TransactionIds == null) return;

            using var scope = _scopeFactory.CreateScope();
            var lifecycle = scope.ServiceProvider.GetRequiredService<ITransactionLifecycleService>();

            foreach (var txId in evt.TransactionIds)
            {
                await lifecycle.MarkSealedAsync(txId, (long)evt.DocketId, ct);
            }

            _logger.LogDebug("Processed docket:confirmed for {Count} transactions in docket {Docket}",
                evt.TransactionIds.Count, evt.DocketId);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse docket:confirmed event");
        }
    }

    private async Task HandleReceiptGeneratedAsync(string message, CancellationToken ct)
    {
        // Parse the event — expected format:
        // JSON with RegisterId, TransactionId, DocketNumber, ReceiptId, SealedAt
        try
        {
            var evt = System.Text.Json.JsonSerializer.Deserialize<ReceiptGeneratedEvent>(message,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (evt == null) return;

            using var scope = _scopeFactory.CreateScope();
            var lifecycle = scope.ServiceProvider.GetRequiredService<ITransactionLifecycleService>();

            await lifecycle.MarkReceiptedAsync(evt.TransactionId, evt.ReceiptId, ct);

            _logger.LogDebug("Processed receipt:generated for transaction {TxId}",
                evt.TransactionId);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse receipt:generated event");
        }
    }

    // Event DTOs — match the format published by Register Service
    private record DocketConfirmedEvent
    {
        public string RegisterId { get; init; } = string.Empty;
        public ulong DocketId { get; init; }
        public List<string> TransactionIds { get; init; } = [];
        public string Hash { get; init; } = string.Empty;
    }

    private record ReceiptGeneratedEvent
    {
        public string RegisterId { get; init; } = string.Empty;
        public string TransactionId { get; init; } = string.Empty;
        public long DocketNumber { get; init; }
        public string ReceiptId { get; init; } = string.Empty;
        public DateTimeOffset SealedAt { get; init; }
    }
}
