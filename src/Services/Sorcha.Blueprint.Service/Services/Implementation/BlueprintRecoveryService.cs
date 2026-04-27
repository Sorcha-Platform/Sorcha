// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models;
using Sorcha.ServiceClients.Register;

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

    /// <summary>Initialises a new instance of the <see cref="BlueprintRecoveryService"/> class.</summary>
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
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
        var publishedStore = scope.ServiceProvider.GetRequiredService<IPublishedBlueprintStore>();

        // Step 1: Discover all registers
        var registers = await DiscoverRegistersAsync(registerClient, cancellationToken);
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

            // After initial recovery, skip registers that have exceeded the retry cap.
            // The cap does not apply during initial recovery — keep trying so a slow
            // register still seeds blueprints once it comes online.
            if (_recoveryState.IsComplete
                && state.ConsecutiveFailures >= _options.MaxRetryAttempts)
            {
                _logger.LogDebug(
                    "Skipping {RegisterId} ({RegisterName}): exceeded {MaxRetryAttempts} retries",
                    registerId, registerName, _options.MaxRetryAttempts);
                continue;
            }

            state.LastCheckedAt = DateTimeOffset.UtcNow;

            try
            {
                var result = await RecoverFromRegisterAsync(
                    registerClient, publishedStore, registerId, cancellationToken);

                state.Status = RegisterHealthStatus.Online;
                state.Height = (int)result.Height;
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
        IRegisterServiceClient registerClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var registers = await registerClient.GetInternalRegistersAsync(cancellationToken);
            return registers.Select(r => (r.Id, r.Name)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover registers");
            return [];
        }
    }

    private async Task<(long Height, int BlueprintCount)> RecoverFromRegisterAsync(
        IRegisterServiceClient registerClient,
        IPublishedBlueprintStore publishedStore,
        string registerId,
        CancellationToken cancellationToken)
    {
        var result = await registerClient.GetPublishedBlueprintsAsync(registerId, cancellationToken);

        // Typed client returns null on failure (network error, non-success status).
        // Treat as failure so the caller marks the register Offline.
        if (result is null)
        {
            throw new InvalidOperationException(
                $"Failed to fetch published blueprints for register {registerId}");
        }

        if (result.Blueprints.Count == 0)
        {
            return (result.RegisterHeight, 0);
        }

        // Dedup by BlueprintId within the response — take the latest by PublishedAt.
        // The register may carry multiple publish events for the same blueprint id.
        var latestPerBlueprint = result.Blueprints
            .GroupBy(b => b.BlueprintId)
            .Select(g => g.OrderByDescending(b => b.PublishedAt).First());

        var count = 0;
        foreach (var bp in latestPerBlueprint)
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
}
