// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Fetches blueprints from the Blueprint Service.
/// Used by BlueprintCache to retrieve blueprints when not in cache.
/// </summary>
public class BlueprintFetcher : IBlueprintFetcher
{
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly BlueprintFetcherConfiguration _config;
    private readonly ILogger<BlueprintFetcher> _logger;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Statistics
    private long _totalFetched;
    private long _totalFailures;
    private long _totalValidations;
    private readonly List<double> _fetchTimes = new();
    private readonly object _statsLock = new();
    private DateTimeOffset? _lastFetchedAt;

    public BlueprintFetcher(
        IBlueprintServiceClient blueprintClient,
        IOptions<BlueprintFetcherConfiguration> config,
        ILogger<BlueprintFetcher> logger)
    {
        _blueprintClient = blueprintClient ?? throw new ArgumentNullException(nameof(blueprintClient));
        _config = config?.Value ?? new BlueprintFetcherConfiguration();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> FetchBlueprintAsync(
        string blueprintId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        var sw = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Fetching blueprint {BlueprintId} from Blueprint Service", blueprintId);

            // Fetch blueprint JSON from Blueprint Service
            var blueprintJson = await _blueprintClient.GetBlueprintAsync(blueprintId, ct);

            if (string.IsNullOrEmpty(blueprintJson))
            {
                _logger.LogWarning("Blueprint {BlueprintId} not found in Blueprint Service", blueprintId);
                Interlocked.Increment(ref _totalFailures);
                return null;
            }

            // Deserialize to blueprint model
            var blueprint = JsonSerializer.Deserialize<BlueprintModel>(blueprintJson, _jsonOptions);

            if (blueprint == null)
            {
                _logger.LogWarning(
                    "Failed to deserialize blueprint {BlueprintId}",
                    blueprintId);
                Interlocked.Increment(ref _totalFailures);
                return null;
            }

            sw.Stop();

            Interlocked.Increment(ref _totalFetched);
            RecordFetchTime(sw.Elapsed.TotalMilliseconds);
            _lastFetchedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Fetched blueprint {BlueprintId} ({Title}) in {ElapsedMs}ms",
                blueprintId, blueprint.Title, sw.ElapsedMilliseconds);

            return blueprint;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            _logger.LogError(
                ex,
                "Error fetching blueprint {BlueprintId} from Blueprint Service",
                blueprintId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> FetchBlueprintByHashAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execDefHash);

        var sw = Stopwatch.StartNew();

        try
        {
            var blueprintJson = await _blueprintClient.GetBlueprintDefinitionAsync(blueprintId, execDefHash, ct);

            if (string.IsNullOrEmpty(blueprintJson))
            {
                // Deliberately a WARNING, not a debug line. An unresolvable pin means a running
                // instance cannot advance on this node, and the operator-visible symptom is a
                // transaction that never seals — which says nothing about the cause on its own.
                _logger.LogWarning(
                    "Pinned definition {ExecDefHash} of blueprint {BlueprintId} could not be resolved " +
                    "from the Blueprint Service. An instance pinned to it cannot advance on this node.",
                    execDefHash, blueprintId);
                Interlocked.Increment(ref _totalFailures);
                return null;
            }

            var blueprint = JsonSerializer.Deserialize<BlueprintModel>(blueprintJson, _jsonOptions);
            if (blueprint is null)
            {
                _logger.LogWarning(
                    "Failed to deserialize pinned definition {ExecDefHash} of blueprint {BlueprintId}",
                    execDefHash, blueprintId);
                Interlocked.Increment(ref _totalFailures);
                return null;
            }

            sw.Stop();
            Interlocked.Increment(ref _totalFetched);
            RecordFetchTime(sw.Elapsed.TotalMilliseconds);
            _lastFetchedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Fetched pinned definition {ExecDefHash} of blueprint {BlueprintId} in {ElapsedMs}ms",
                execDefHash, blueprintId, sw.ElapsedMilliseconds);

            return blueprint;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalFailures);
            _logger.LogError(ex,
                "Error fetching pinned definition {ExecDefHash} of blueprint {BlueprintId}",
                execDefHash, blueprintId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<BlueprintPayloadValidationResult> ValidatePayloadAsync(
        string blueprintId,
        string actionId,
        string payloadJson,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        Interlocked.Increment(ref _totalValidations);

        try
        {
            _logger.LogDebug(
                "Validating payload for blueprint {BlueprintId} action {ActionId}",
                blueprintId, actionId);

            // Validate via Blueprint Service
            var isValid = await _blueprintClient.ValidatePayloadAsync(
                blueprintId,
                actionId,
                payloadJson,
                ct);

            return new BlueprintPayloadValidationResult
            {
                IsValid = isValid,
                BlueprintFound = true,
                ActionFound = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error validating payload for blueprint {BlueprintId} action {ActionId}",
                blueprintId, actionId);

            return new BlueprintPayloadValidationResult
            {
                IsValid = false,
                Errors = new[] { $"Validation error: {ex.Message}" }
            };
        }
    }

    /// <inheritdoc/>
    public BlueprintFetcherStats GetStats()
    {
        double avgFetchTime;
        lock (_statsLock)
        {
            avgFetchTime = _fetchTimes.Count > 0 ? _fetchTimes.Average() : 0;
        }

        return new BlueprintFetcherStats
        {
            TotalFetched = Interlocked.Read(ref _totalFetched),
            TotalFailures = Interlocked.Read(ref _totalFailures),
            TotalValidations = Interlocked.Read(ref _totalValidations),
            AverageFetchTimeMs = avgFetchTime,
            LastFetchedAt = _lastFetchedAt
        };
    }

    private void RecordFetchTime(double timeMs)
    {
        lock (_statsLock)
        {
            _fetchTimes.Add(timeMs);
            // Keep only last 100 samples
            if (_fetchTimes.Count > 100)
            {
                _fetchTimes.RemoveAt(0);
            }
        }
    }
}
