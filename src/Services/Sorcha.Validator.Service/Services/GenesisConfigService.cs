// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Redis-backed genesis configuration cache with optional local L1 cache.
/// Provides fast access to consensus, validator, and leader election configurations.
/// </summary>
public class GenesisConfigService : IGenesisConfigService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly IRegisterServiceClient _registerClient;
    private readonly GenesisConfigCacheConfiguration _config;
    private readonly ILogger<GenesisConfigService> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;

    // L1 local cache
    private readonly ConcurrentDictionary<string, LocalCacheEntry> _localCache = new();

    // Version tracking for staleness checks
    private readonly ConcurrentDictionary<string, string> _versionCache = new();

    /// <inheritdoc/>
    public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    public GenesisConfigService(
        IConnectionMultiplexer redis,
        IRegisterServiceClient registerClient,
        IOptions<GenesisConfigCacheConfiguration> config,
        ILogger<GenesisConfigService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _redis.GetDatabase();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _pipeline = BuildResiliencePipeline();
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = _config.MaxRetries,
                Delay = _config.RetryDelay,
                BackoffType = DelayBackoffType.Exponential
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();
    }


    private string GetConfigKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:config";

    private string GetVersionKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:version";

    private string GetLastCheckKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:lastcheck";


    /// <inheritdoc/>
    public async Task<ConsensusConfig> GetConsensusConfigAsync(
        string registerId,
        CancellationToken ct = default)
    {
        var fullConfig = await GetFullConfigAsync(registerId, ct);
        return fullConfig.Consensus;
    }

    /// <inheritdoc/>
    public async Task<ValidatorConfig> GetValidatorConfigAsync(
        string registerId,
        CancellationToken ct = default)
    {
        var fullConfig = await GetFullConfigAsync(registerId, ct);
        return fullConfig.Validators;
    }

    /// <inheritdoc/>
    public async Task<LeaderElectionConfig> GetLeaderElectionConfigAsync(
        string registerId,
        CancellationToken ct = default)
    {
        var fullConfig = await GetFullConfigAsync(registerId, ct);
        return fullConfig.LeaderElection;
    }

    /// <inheritdoc/>
    public async Task<GenesisConfiguration> GetFullConfigAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        try
        {
            // Check L1 local cache first
            if (_config.EnableLocalCache && TryGetFromLocalCache(registerId, out var localConfig))
            {
                return localConfig!;
            }

            // Check L2 Redis cache
            var config = await GetFromRedisAsync(registerId, ct);

            if (config != null)
            {
                // Populate L1 cache
                if (_config.EnableLocalCache)
                {
                    SetInLocalCache(registerId, config);
                }

                // Track version for change detection
                _versionCache[registerId] = config.ControlBlueprintVersionId;

                return config;
            }

            // Not in cache - need to fetch from source and cache
            config = await FetchAndCacheConfigAsync(registerId, ct);

            if (config == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load genesis configuration for register {registerId}");
            }

            return config;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to get genesis config for register {RegisterId}", registerId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsConfigStaleAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        try
        {
            // Check when we last checked for staleness
            var lastCheckKey = GetLastCheckKey(registerId);
            var lastCheckValue = await _database.StringGetAsync(lastCheckKey);

            if (!lastCheckValue.IsNullOrEmpty)
            {
                var lastCheck = DateTimeOffset.Parse(lastCheckValue.ToString());
                if (DateTimeOffset.UtcNow - lastCheck < _config.StaleCheckInterval)
                {
                    // Recently checked, not stale
                    return false;
                }
            }

            // Check if version has changed
            // In production, this would query the Register Service for the latest
            // control blueprint version and compare with cached version
            var versionKey = GetVersionKey(registerId);
            var cachedVersion = await _database.StringGetAsync(versionKey);

            if (cachedVersion.IsNullOrEmpty)
            {
                // No version cached, needs refresh
                return true;
            }

            // Update last check timestamp
            await _database.StringSetAsync(
                lastCheckKey,
                DateTimeOffset.UtcNow.ToString("O"),
                _config.StaleCheckInterval);

            // For now, assume not stale unless explicitly invalidated
            // In production, would compare with actual source version
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check staleness for register {RegisterId}", registerId);
            // Assume stale on error to trigger refresh
            return true;
        }
    }

    /// <inheritdoc/>
    public async Task RefreshConfigAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        _logger.LogInformation("Refreshing genesis config for register {RegisterId}", registerId);

        // Get current version for change detection
        string? previousVersionId = null;
        if (_versionCache.TryGetValue(registerId, out var cached))
        {
            previousVersionId = cached;
        }

        // Remove from L1 cache
        _localCache.TryRemove(registerId, out _);

        // Remove from L2 cache
        var configKey = GetConfigKey(registerId);
        await _database.KeyDeleteAsync(configKey);

        // Fetch fresh config
        var newConfig = await FetchAndCacheConfigAsync(registerId, ct);

        // Raise event if version changed
        if (newConfig != null && previousVersionId != null &&
            previousVersionId != newConfig.ControlBlueprintVersionId)
        {
            RaiseConfigChanged(registerId, previousVersionId, newConfig);
        }
    }


    private async Task<GenesisConfiguration?> GetFromRedisAsync(
        string registerId,
        CancellationToken ct)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetConfigKey(registerId);
                var json = await _database.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                    return null;

                return JsonSerializer.Deserialize<GenesisConfiguration>(
                    json.ToString(), _jsonOptions);
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "Circuit breaker open, cannot fetch genesis config {RegisterId}",
                registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Redis error fetching genesis config {RegisterId}",
                registerId);
            return null;
        }
    }

    private async Task<GenesisConfiguration?> FetchAndCacheConfigAsync(
        string registerId,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Fetching genesis config from Register Service for register {RegisterId}",
            registerId);

        try
        {
            // Step 1: Verify register exists
            var register = await _registerClient.GetRegisterAsync(registerId, ct);
            if (register == null)
            {
                _logger.LogWarning("Register {RegisterId} not found, using default config", registerId);
                return await CreateAndCacheDefaultConfigAsync(registerId, ct);
            }

            // Step 2: Try to read genesis docket (docket 0)
            var genesisDocket = await _registerClient.ReadDocketAsync(registerId, 0, ct);
            if (genesisDocket == null || genesisDocket.Transactions.Count == 0)
            {
                _logger.LogDebug(
                    "No genesis docket found for register {RegisterId}, using default config",
                    registerId);
                return await CreateAndCacheDefaultConfigAsync(registerId, ct);
            }

            // Step 3: Get the first transaction (genesis transaction)
            var genesisTransaction = genesisDocket.Transactions.FirstOrDefault();
            if (genesisTransaction == null)
            {
                _logger.LogDebug(
                    "No genesis transaction in docket for register {RegisterId}, using default config",
                    registerId);
                return await CreateAndCacheDefaultConfigAsync(registerId, ct);
            }

            // Step 4: Parse control blueprint from transaction payload
            var config = ParseGenesisConfiguration(registerId, genesisTransaction, genesisDocket);

            // Step 5: Cache the configuration
            await CacheConfigAsync(config, ct);

            _logger.LogInformation(
                "Loaded genesis config for register {RegisterId} from transaction {TxId}",
                registerId, genesisTransaction.TxId ?? genesisTransaction.Id);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch genesis config from Register Service for {RegisterId}, using default",
                registerId);
            return await CreateAndCacheDefaultConfigAsync(registerId, ct);
        }
    }

    private async Task<GenesisConfiguration> CreateAndCacheDefaultConfigAsync(
        string registerId,
        CancellationToken ct)
    {
        var config = CreateDefaultConfiguration(registerId);
        await CacheConfigAsync(config, ct);
        return config;
    }

    private GenesisConfiguration ParseGenesisConfiguration(
        string registerId,
        TransactionModel genesisTransaction,
        DocketModel genesisDocket)
    {
        // Try to parse control blueprint from transaction payloads
        try
        {
            // Check if transaction has any payloads
            if (genesisTransaction.Payloads != null && genesisTransaction.Payloads.Length > 0)
            {
                // Get the first payload (typically the control record)
                var firstPayload = genesisTransaction.Payloads[0];

                if (!string.IsNullOrEmpty(firstPayload.Data))
                {
                    var payloadData = JsonSerializer.Deserialize<JsonElement>(firstPayload.Data, _jsonOptions);

                    // Tier 1: Check for RegisterPolicy on the control record (Feature 048)
                    if (payloadData.TryGetProperty("registerPolicy", out var registerPolicyElement))
                    {
                        _logger.LogDebug(
                            "Found RegisterPolicy on control record for register {RegisterId}",
                            registerId);
                        return ParseFromRegisterPolicy(registerId, genesisTransaction, genesisDocket, registerPolicyElement);
                    }

                    // Tier 2: Legacy — look for control blueprint configuration in payload
                    if (payloadData.TryGetProperty("controlBlueprint", out var controlBlueprint) ||
                        payloadData.TryGetProperty("configuration", out controlBlueprint))
                    {
                        return ParseControlBlueprint(registerId, genesisTransaction, genesisDocket, controlBlueprint);
                    }

                    // Check if the payload itself is the control record with legacy consensus config
                    if (payloadData.TryGetProperty("consensus", out _) &&
                        !payloadData.TryGetProperty("registerPolicy", out _))
                    {
                        return ParseControlBlueprint(registerId, genesisTransaction, genesisDocket, payloadData);
                    }
                }
            }

            // Also check MetaData for configuration (alternative location)
            if (genesisTransaction.MetaData != null)
            {
                _logger.LogDebug(
                    "Genesis transaction has metadata, checking for control config in register {RegisterId}",
                    registerId);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse genesis payload for register {RegisterId}, using defaults",
                registerId);
        }

        // Fallback to default configuration with genesis transaction reference
        _logger.LogDebug(
            "Using default config for register {RegisterId} (no control blueprint in genesis)",
            registerId);

        return new GenesisConfiguration
        {
            RegisterId = registerId,
            GenesisTransactionId = genesisTransaction.TxId ?? genesisTransaction.Id ?? $"genesis-{registerId}",
            ControlBlueprintVersionId = $"control-v1-{registerId}",
            Consensus = CreateDefaultConsensusConfig(),
            Validators = CreateDefaultValidatorConfig(),
            LeaderElection = CreateDefaultLeaderElectionConfig(),
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(30)
        };
    }

    private GenesisConfiguration ParseControlBlueprint(
        string registerId,
        TransactionModel genesisTransaction,
        DocketModel genesisDocket,
        JsonElement config)
    {
        var consensusConfig = CreateDefaultConsensusConfig();
        var validatorConfig = CreateDefaultValidatorConfig();
        var leaderElectionConfig = CreateDefaultLeaderElectionConfig();

        // Parse consensus configuration
        if (config.TryGetProperty("consensus", out var consensus))
        {
            consensusConfig = ParseConsensusConfig(consensus);
        }

        // Parse validator configuration
        if (config.TryGetProperty("validators", out var validators))
        {
            validatorConfig = ParseValidatorConfig(validators);
        }

        // Parse leader election configuration
        if (config.TryGetProperty("leaderElection", out var leaderElection))
        {
            leaderElectionConfig = ParseLeaderElectionConfig(leaderElection);
        }

        return new GenesisConfiguration
        {
            RegisterId = registerId,
            GenesisTransactionId = genesisTransaction.TxId ?? genesisTransaction.Id ?? $"genesis-{registerId}",
            ControlBlueprintVersionId = genesisDocket.DocketId,
            Consensus = consensusConfig,
            Validators = validatorConfig,
            LeaderElection = leaderElectionConfig,
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Parses GenesisConfiguration from the new RegisterPolicy model (Feature 048 - Tier 1)
    /// </summary>
    private GenesisConfiguration ParseFromRegisterPolicy(
        string registerId,
        TransactionModel genesisTransaction,
        DocketModel genesisDocket,
        JsonElement registerPolicy)
    {
        var consensusConfig = CreateDefaultConsensusConfig();
        var validatorConfig = CreateDefaultValidatorConfig();
        var leaderElectionConfig = CreateDefaultLeaderElectionConfig();

        // Parse consensus from RegisterPolicy
        if (registerPolicy.TryGetProperty("consensus", out var consensus))
        {
            if (consensus.TryGetProperty("signatureThresholdMin", out var sigMin) && sigMin.TryGetInt32(out var sigMinVal))
                consensusConfig = consensusConfig with { SignatureThresholdMin = sigMinVal };
            if (consensus.TryGetProperty("signatureThresholdMax", out var sigMax) && sigMax.TryGetInt32(out var sigMaxVal))
                consensusConfig = consensusConfig with { SignatureThresholdMax = sigMaxVal };
            if (consensus.TryGetProperty("maxTransactionsPerDocket", out var maxTx) && maxTx.TryGetInt32(out var maxTxVal))
                consensusConfig = consensusConfig with { MaxTransactionsPerDocket = maxTxVal };
            if (consensus.TryGetProperty("docketBuildIntervalMs", out var buildInterval) && buildInterval.TryGetInt32(out var buildIntervalVal))
                consensusConfig = consensusConfig with { DocketBuildInterval = TimeSpan.FromMilliseconds(buildIntervalVal) };
            if (consensus.TryGetProperty("docketTimeoutSeconds", out var timeout) && timeout.TryGetInt32(out var timeoutVal))
                consensusConfig = consensusConfig with { DocketTimeout = TimeSpan.FromSeconds(timeoutVal) };
        }

        // Parse validators from RegisterPolicy
        if (registerPolicy.TryGetProperty("validators", out var validators))
        {
            if (validators.TryGetProperty("registrationMode", out var mode))
            {
                var modeStr = mode.ValueKind == JsonValueKind.String
                    ? mode.GetString()?.ToLowerInvariant()
                    : mode.GetInt32() == 0 ? "public" : "consent";
                validatorConfig = validatorConfig with { RegistrationMode = modeStr ?? "public" };
            }
            if (validators.TryGetProperty("minValidators", out var min) && min.TryGetInt32(out var minVal))
                validatorConfig = validatorConfig with { MinValidators = minVal };
            if (validators.TryGetProperty("maxValidators", out var max) && max.TryGetInt32(out var maxVal))
                validatorConfig = validatorConfig with { MaxValidators = maxVal };
            if (validators.TryGetProperty("requireStake", out var stake))
                validatorConfig = validatorConfig with { RequireStake = stake.GetBoolean() };
            if (validators.TryGetProperty("stakeAmount", out var stakeAmt) && stakeAmt.ValueKind == JsonValueKind.Number)
                validatorConfig = validatorConfig with { StakeAmount = stakeAmt.GetDecimal() };
        }

        // Parse leader election from RegisterPolicy
        if (registerPolicy.TryGetProperty("leaderElection", out var leaderElection))
        {
            if (leaderElection.TryGetProperty("mechanism", out var mechanism))
            {
                var mechStr = mechanism.ValueKind == JsonValueKind.String
                    ? mechanism.GetString()?.ToLowerInvariant()
                    : mechanism.GetInt32() switch { 0 => "rotating", 1 => "raft", 2 => "stake-weighted", _ => "rotating" };
                leaderElectionConfig = leaderElectionConfig with { Mechanism = mechStr ?? "rotating" };
            }
            if (leaderElection.TryGetProperty("heartbeatIntervalMs", out var heartbeat) && heartbeat.TryGetInt32(out var heartbeatVal))
                leaderElectionConfig = leaderElectionConfig with { HeartbeatInterval = TimeSpan.FromMilliseconds(heartbeatVal) };
            if (leaderElection.TryGetProperty("leaderTimeoutMs", out var leaderTimeout) && leaderTimeout.TryGetInt32(out var leaderTimeoutVal))
                leaderElectionConfig = leaderElectionConfig with { LeaderTimeout = TimeSpan.FromMilliseconds(leaderTimeoutVal) };
            if (leaderElection.TryGetProperty("termDurationSeconds", out var termDuration) && termDuration.TryGetInt32(out var termDurationVal))
                leaderElectionConfig = leaderElectionConfig with { TermDuration = TimeSpan.FromSeconds(termDurationVal) };
        }

        var versionId = "policy-v1";
        if (registerPolicy.TryGetProperty("version", out var version))
        {
            versionId = $"policy-v{version.GetUInt32()}";
        }

        return new GenesisConfiguration
        {
            RegisterId = registerId,
            GenesisTransactionId = genesisTransaction.TxId ?? genesisTransaction.Id ?? $"genesis-{registerId}",
            ControlBlueprintVersionId = versionId,
            Consensus = consensusConfig,
            Validators = validatorConfig,
            LeaderElection = leaderElectionConfig,
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(30)
        };
    }

    private ConsensusConfig ParseConsensusConfig(JsonElement element)
    {
        var config = CreateDefaultConsensusConfig();

        if (element.TryGetProperty("signatureThreshold", out var threshold))
        {
            if (threshold.TryGetProperty("min", out var min) && min.TryGetInt32(out var minVal))
                config = config with { SignatureThresholdMin = minVal };
            if (threshold.TryGetProperty("max", out var max) && max.TryGetInt32(out var maxVal))
                config = config with { SignatureThresholdMax = maxVal };
        }

        if (element.TryGetProperty("docketTimeout", out var timeout))
            config = config with { DocketTimeout = ParseDuration(timeout.GetString()) ?? config.DocketTimeout };

        if (element.TryGetProperty("maxSignaturesPerDocket", out var maxSigs) && maxSigs.TryGetInt32(out var maxSigsVal))
            config = config with { MaxSignaturesPerDocket = maxSigsVal };

        if (element.TryGetProperty("maxTransactionsPerDocket", out var maxTx) && maxTx.TryGetInt32(out var maxTxVal))
            config = config with { MaxTransactionsPerDocket = maxTxVal };

        if (element.TryGetProperty("docketBuildInterval", out var interval))
            config = config with { DocketBuildInterval = ParseDuration(interval.GetString()) ?? config.DocketBuildInterval };

        return config;
    }

    private ValidatorConfig ParseValidatorConfig(JsonElement element)
    {
        var config = CreateDefaultValidatorConfig();

        if (element.TryGetProperty("registrationMode", out var mode))
            config = config with { RegistrationMode = mode.GetString() ?? "public" };

        if (element.TryGetProperty("minValidators", out var min) && min.TryGetInt32(out var minVal))
            config = config with { MinValidators = minVal };

        if (element.TryGetProperty("maxValidators", out var max) && max.TryGetInt32(out var maxVal))
            config = config with { MaxValidators = maxVal };

        if (element.TryGetProperty("requireStake", out var stake))
            config = config with { RequireStake = stake.GetBoolean() };

        return config;
    }

    private LeaderElectionConfig ParseLeaderElectionConfig(JsonElement element)
    {
        var config = CreateDefaultLeaderElectionConfig();

        if (element.TryGetProperty("mechanism", out var mechanism))
            config = config with { Mechanism = mechanism.GetString() ?? "rotating" };

        if (element.TryGetProperty("heartbeatInterval", out var heartbeat))
            config = config with { HeartbeatInterval = ParseDuration(heartbeat.GetString()) ?? config.HeartbeatInterval };

        if (element.TryGetProperty("leaderTimeout", out var timeout))
            config = config with { LeaderTimeout = ParseDuration(timeout.GetString()) ?? config.LeaderTimeout };

        return config;
    }

    private static TimeSpan? ParseDuration(string? iso8601)
    {
        if (string.IsNullOrEmpty(iso8601))
            return null;

        try
        {
            // Parse ISO 8601 duration (e.g., "PT30S", "PT5M", "PT1H")
            return System.Xml.XmlConvert.ToTimeSpan(iso8601);
        }
        catch
        {
            return null;
        }
    }

    private static ConsensusConfig CreateDefaultConsensusConfig() => new()
    {
        SignatureThresholdMin = 2,
        SignatureThresholdMax = 10,
        DocketTimeout = TimeSpan.FromSeconds(30),
        MaxSignaturesPerDocket = 100,
        MaxTransactionsPerDocket = 1000,
        DocketBuildInterval = TimeSpan.FromMilliseconds(100)
    };

    private static ValidatorConfig CreateDefaultValidatorConfig() => new()
    {
        RegistrationMode = "public",
        MinValidators = 1,
        MaxValidators = 100,
        RequireStake = false,
        StakeAmount = null
    };

    private static LeaderElectionConfig CreateDefaultLeaderElectionConfig() => new()
    {
        Mechanism = "rotating",
        HeartbeatInterval = TimeSpan.FromSeconds(1),
        LeaderTimeout = TimeSpan.FromSeconds(5),
        TermDuration = TimeSpan.FromMinutes(1)
    };

    private async Task CacheConfigAsync(
        GenesisConfiguration config,
        CancellationToken ct)
    {
        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var configKey = GetConfigKey(config.RegisterId);
                var versionKey = GetVersionKey(config.RegisterId);

                var json = JsonSerializer.Serialize(config, _jsonOptions);

                // Store config and version
                var batch = _database.CreateBatch();
                _ = batch.StringSetAsync(configKey, json, config.CacheTtl);
                _ = batch.StringSetAsync(versionKey, config.ControlBlueprintVersionId, config.CacheTtl);
                batch.Execute();

                await Task.CompletedTask;
            }, ct);

            // Update version tracking
            _versionCache[config.RegisterId] = config.ControlBlueprintVersionId;

            // Update L1 cache
            if (_config.EnableLocalCache)
            {
                SetInLocalCache(config.RegisterId, config);
            }

            _logger.LogDebug(
                "Cached genesis config for register {RegisterId}, version {Version}",
                config.RegisterId, config.ControlBlueprintVersionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to cache genesis config for register {RegisterId}",
                config.RegisterId);
        }
    }

    private static GenesisConfiguration CreateDefaultConfiguration(string registerId)
    {
        return new GenesisConfiguration
        {
            RegisterId = registerId,
            GenesisTransactionId = $"genesis-{registerId}",
            ControlBlueprintVersionId = $"control-v1-{registerId}",
            Consensus = CreateDefaultConsensusConfig(),
            Validators = CreateDefaultValidatorConfig(),
            LeaderElection = CreateDefaultLeaderElectionConfig(),
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(30)
        };
    }

    private bool TryGetFromLocalCache(
        string registerId,
        out GenesisConfiguration? config)
    {
        config = null;

        if (!_localCache.TryGetValue(registerId, out var entry))
            return false;

        // Check if expired
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _localCache.TryRemove(registerId, out _);
            return false;
        }

        config = entry.Config;
        return true;
    }

    private void SetInLocalCache(string registerId, GenesisConfiguration config)
    {
        // Enforce max entries limit
        if (_localCache.Count >= _config.LocalCacheMaxEntries)
        {
            // Remove oldest entries
            var toRemove = _localCache
                .OrderBy(kvp => kvp.Value.ExpiresAt)
                .Take(_localCache.Count - _config.LocalCacheMaxEntries + 1)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _localCache.TryRemove(key, out _);
            }
        }

        var entry = new LocalCacheEntry
        {
            Config = config,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_config.LocalCacheTtl)
        };

        _localCache[registerId] = entry;
    }

    private void RaiseConfigChanged(
        string registerId,
        string previousVersionId,
        GenesisConfiguration newConfig)
    {
        var changedProperties = new List<string>();

        // In production, would compare old vs new config to determine what changed
        // For now, just note that version changed
        changedProperties.Add("ControlBlueprintVersion");

        var args = new ConfigChangedEventArgs
        {
            RegisterId = registerId,
            PreviousVersionId = previousVersionId,
            NewVersionId = newConfig.ControlBlueprintVersionId,
            ChangedProperties = changedProperties
        };

        _logger.LogInformation(
            "Genesis config changed for register {RegisterId}: {Previous} -> {New}",
            registerId, previousVersionId, newConfig.ControlBlueprintVersionId);

        ConfigChanged?.Invoke(this, args);
    }



    private class LocalCacheEntry
    {
        public required GenesisConfiguration Config { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }

}
