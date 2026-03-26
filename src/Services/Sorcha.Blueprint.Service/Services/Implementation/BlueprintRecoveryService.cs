// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Background service that recovers published blueprint state from the register ledger
/// on startup and periodically refreshes register status and blueprint discovery.
/// </summary>
public class BlueprintRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RecoveryState _recoveryState;
    private readonly RecoveryOptions _options;
    private readonly ILogger<BlueprintRecoveryService> _logger;

    public BlueprintRecoveryService(
        IServiceScopeFactory scopeFactory,
        RecoveryState recoveryState,
        IOptions<RecoveryOptions> options,
        ILogger<BlueprintRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _recoveryState = recoveryState;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _recoveryState.StartedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Blueprint recovery service starting");

        // Initial recovery
        await RunRecoveryAsync(stoppingToken);

        _recoveryState.IsComplete = true;
        _recoveryState.CompletedAt = DateTimeOffset.UtcNow;

        var onlineCount = _recoveryState.RegisterStates.Values.Count(r => r.Status == RegisterHealthStatus.Online);
        var offlineCount = _recoveryState.RegisterStates.Values.Count(r => r.Status == RegisterHealthStatus.Offline);
        var totalBlueprints = _recoveryState.RegisterStates.Values.Sum(r => r.RecoveredBlueprintCount);

        _logger.LogInformation(
            "Blueprint recovery complete. Registers: {Online} online, {Offline} offline. Blueprints recovered: {Total}",
            onlineCount, offlineCount, totalBlueprints);

        // Periodic refresh loop
        var refreshInterval = TimeSpan.FromSeconds(_options.RefreshIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(refreshInterval, stoppingToken);

            _logger.LogDebug("Running periodic register refresh");
            await RunRecoveryAsync(stoppingToken);
        }
    }

    internal async Task RunRecoveryAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var publishedStore = scope.ServiceProvider.GetRequiredService<IPublishedBlueprintStore>();

        // Step 1: Discover all registers
        var registers = await DiscoverRegistersAsync(httpClientFactory, cancellationToken);
        if (registers.Count == 0)
        {
            _logger.LogWarning("No registers discovered during recovery");
            return;
        }

        _logger.LogInformation("Discovered {Count} registers for recovery", registers.Count);

        // Step 2: For each register, recover published blueprints
        foreach (var (registerId, registerName) in registers)
        {
            var state = _recoveryState.RegisterStates.GetOrAdd(registerId, _ => new RegisterRecoveryState
            {
                RegisterId = registerId,
                RegisterName = registerName
            });

            state.LastCheckedAt = DateTimeOffset.UtcNow;

            try
            {
                var result = await RecoverFromRegisterAsync(
                    httpClientFactory, publishedStore, registerId, cancellationToken);

                state.Status = RegisterHealthStatus.Online;
                state.Height = result.Height;
                state.RecoveredBlueprintCount = result.BlueprintCount;
                state.LastSuccessAt = DateTimeOffset.UtcNow;
                state.ConsecutiveFailures = 0;
                state.ErrorMessage = null;

                if (result.BlueprintCount > 0)
                {
                    _logger.LogInformation(
                        "Recovered {Count} published blueprints from register {RegisterId} ({RegisterName})",
                        result.BlueprintCount, registerId, registerName);
                }
            }
            catch (Exception ex)
            {
                state.Status = RegisterHealthStatus.Offline;
                state.ConsecutiveFailures++;
                state.ErrorMessage = ex.Message;

                _logger.LogWarning(ex,
                    "Failed to recover from register {RegisterId} ({RegisterName}). Consecutive failures: {Failures}",
                    registerId, registerName, state.ConsecutiveFailures);
            }
        }
    }

    private async Task<List<(string Id, string Name)>> DiscoverRegistersAsync(
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("RegisterService");
            var response = await client.GetAsync("/api/registers", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Register discovery failed with status {StatusCode}", response.StatusCode);
                return [];
            }

            var registers = await response.Content.ReadFromJsonAsync<List<RegisterInfo>>(cancellationToken: cancellationToken);
            return registers?.Select(r => (r.Id, r.Name)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover registers");
            return [];
        }
    }

    private async Task<(int Height, int BlueprintCount)> RecoverFromRegisterAsync(
        IHttpClientFactory httpClientFactory,
        IPublishedBlueprintStore publishedStore,
        string registerId,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("RegisterService");
        var response = await client.GetAsync(
            $"/api/registers/{registerId}/blueprints/published",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PublishedBlueprintsResponse>(
            cancellationToken: cancellationToken);

        if (result?.Blueprints is null || result.Blueprints.Count == 0)
        {
            return (result?.RegisterHeight ?? 0, 0);
        }

        var count = 0;
        foreach (var bp in result.Blueprints)
        {
            try
            {
                var blueprint = JsonSerializer.Deserialize<Sorcha.Blueprint.Models.Blueprint>(bp.BlueprintJson);
                if (blueprint is null) continue;

                // Check if already in store (idempotent)
                var existing = await publishedStore.GetVersionsAsync(bp.BlueprintId);
                if (existing.Any(e => string.Equals(e.RegisterId, registerId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue; // Already recovered
                }

                await publishedStore.AddAsync(new PublishedBlueprint
                {
                    BlueprintId = bp.BlueprintId,
                    Blueprint = blueprint,
                    PublishedAt = bp.PublishedAt,
                    RegisterId = registerId
                });

                count++;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to deserialize blueprint {BlueprintId} from register {RegisterId}",
                    bp.BlueprintId, registerId);
            }
        }

        return (result.RegisterHeight, count);
    }

    // DTOs for deserialization
    private record RegisterInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    private record PublishedBlueprintsResponse
    {
        public string RegisterId { get; init; } = string.Empty;
        public List<PublishedBlueprintEntry> Blueprints { get; init; } = [];
        public int RegisterHeight { get; init; }
    }

    private record PublishedBlueprintEntry
    {
        public string BlueprintId { get; init; } = string.Empty;
        public string TransactionId { get; init; } = string.Empty;
        public string PublishedBy { get; init; } = string.Empty;
        public DateTimeOffset PublishedAt { get; init; }
        public string BlueprintJson { get; init; } = "{}";
    }
}
