// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Redis-backed registry of validators for each register.
/// Manages validator status, ordering, and registration.
/// </summary>
public class ValidatorRegistry : IValidatorRegistry
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IGenesisConfigService _genesisConfig;
    private readonly ValidatorRegistryConfiguration _config;
    private readonly ILogger<ValidatorRegistry> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;

    // MongoDB durable storage (write-through from Redis)
    private readonly IMongoCollection<ValidatorDocument> _validatorsCollection;
    private readonly IMongoCollection<ValidatorAuditEntry> _auditCollection;
    private volatile bool _mongoIndexesCreated;
    private readonly SemaphoreSlim _indexCreationLock = new(1, 1);

    // L1 local cache
    private readonly ConcurrentDictionary<string, LocalCacheEntry> _localCache = new();

    // Cached per-register operational TTL (avoids repeated policy lookups)
    private readonly ConcurrentDictionary<string, (TimeSpan Ttl, DateTimeOffset CachedAt)> _operationalTtlCache = new();

    /// <inheritdoc/>
    public event EventHandler<ValidatorListChangedEventArgs>? ValidatorListChanged;

    public ValidatorRegistry(
        IConnectionMultiplexer redis,
        IMongoClient mongoClient,
        IRegisterServiceClient registerClient,
        IGenesisConfigService genesisConfig,
        IOptions<ValidatorRegistryConfiguration> config,
        ILogger<ValidatorRegistry> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _genesisConfig = genesisConfig ?? throw new ArgumentNullException(nameof(genesisConfig));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _redis.GetDatabase();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _pipeline = BuildResiliencePipeline();

        // MongoDB durable storage
        var mongoDatabase = mongoClient.GetDatabase(_config.MongoDatabase);
        _validatorsCollection = mongoDatabase.GetCollection<ValidatorDocument>(_config.ValidatorsCollection);
        _auditCollection = mongoDatabase.GetCollection<ValidatorAuditEntry>(_config.AuditCollection);
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


    private string GetValidatorsKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:list";

    private string GetValidatorKey(string registerId, string validatorId) =>
        $"{_config.KeyPrefix}{registerId}:validator:{validatorId}";

    private string GetOrderKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:order";


    /// <inheritdoc/>
    public async Task<IReadOnlyList<ValidatorInfo>> GetActiveValidatorsAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        try
        {
            // Check L1 cache first
            if (_config.EnableLocalCache && TryGetFromLocalCache(registerId, out var cached))
            {
                return cached!.Where(v => v.Status == Interfaces.ValidatorStatus.Active).ToList();
            }

            // Fetch from Redis
            var validators = await GetValidatorsFromRedisAsync(registerId, ct);

            // Cache locally
            if (_config.EnableLocalCache && validators.Count > 0)
            {
                SetInLocalCache(registerId, validators);
            }

            return validators.Where(v => v.Status == Interfaces.ValidatorStatus.Active).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active validators for register {RegisterId}", registerId);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsRegisteredAsync(
        string registerId,
        string validatorId,
        CancellationToken ct = default)
    {
        var validator = await GetValidatorAsync(registerId, validatorId, ct);
        return validator != null && validator.Status == Interfaces.ValidatorStatus.Active;
    }

    /// <inheritdoc/>
    public async Task<ValidatorInfo?> GetValidatorAsync(
        string registerId,
        string validatorId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        try
        {
            // Check L1 cache first
            if (_config.EnableLocalCache && TryGetFromLocalCache(registerId, out var cached))
            {
                return cached!.FirstOrDefault(v => v.ValidatorId == validatorId);
            }

            // Fetch from Redis
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetValidatorKey(registerId, validatorId);
                var json = await _database.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                    return null;

                return JsonSerializer.Deserialize<ValidatorInfo>(json.ToString(), _jsonOptions);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get validator {ValidatorId} for register {RegisterId}",
                validatorId, registerId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetValidatorOrderAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetOrderKey(registerId);
                var json = await _database.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                {
                    // Build order from active validators
                    var validators = await GetActiveValidatorsAsync(registerId, token);
                    var order = validators
                        .OrderBy(v => v.OrderIndex ?? int.MaxValue)
                        .ThenBy(v => v.RegisteredAt)
                        .Select(v => v.ValidatorId)
                        .ToList();

                    // Cache the order
                    await _database.StringSetAsync(
                        key,
                        JsonSerializer.Serialize(order, _jsonOptions),
                        _config.CacheTtl);

                    return order;
                }

                return JsonSerializer.Deserialize<List<string>>(json.ToString(), _jsonOptions) ?? [];
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get validator order for register {RegisterId}", registerId);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<ValidatorRegistrationResult> RegisterAsync(
        string registerId,
        ValidatorRegistration registration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(registration);

        try
        {
            _logger.LogInformation(
                "Registering validator {ValidatorId} for register {RegisterId}",
                registration.ValidatorId, registerId);

            // Check if validator configuration allows registration
            var validatorConfig = await _genesisConfig.GetValidatorConfigAsync(registerId, ct);

            // Consent-mode: check on-chain approved validators list
            if (!validatorConfig.IsPublicRegistration)
            {
                var policyResponse = await _registerClient.GetRegisterPolicyAsync(registerId, ct);
                var approvedList = policyResponse?.Policy?.Validators?.ApprovedValidators;

                if (approvedList == null || !approvedList.Any(v =>
                    v.Did.Equals(registration.ValidatorId, StringComparison.OrdinalIgnoreCase) ||
                    v.PublicKey.Equals(registration.PublicKey, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning(
                        "Validator {ValidatorId} rejected: not on approved list for consent-mode register {RegisterId}",
                        registration.ValidatorId, registerId);
                    return ValidatorRegistrationResult.Failed(
                        "Validator is not on the approved list for this consent-mode register");
                }

                _logger.LogInformation(
                    "Validator {ValidatorId} found on approved list for consent-mode register {RegisterId}",
                    registration.ValidatorId, registerId);
            }

            // Check max validators
            var currentCount = await GetActiveCountAsync(registerId, ct);
            if (currentCount >= validatorConfig.MaxValidators)
            {
                return ValidatorRegistrationResult.Failed(
                    $"Maximum validators ({validatorConfig.MaxValidators}) reached");
            }

            // Check if already registered
            var existing = await GetValidatorAsync(registerId, registration.ValidatorId, ct);
            if (existing != null && existing.Status == Interfaces.ValidatorStatus.Active)
            {
                return ValidatorRegistrationResult.Failed("Validator already registered");
            }

            // Determine order index
            var order = await GetValidatorOrderAsync(registerId, ct);
            var orderIndex = order.Count;

            // Create validator info
            var now = DateTimeOffset.UtcNow;
            var validator = new ValidatorInfo
            {
                ValidatorId = registration.ValidatorId,
                PublicKey = registration.PublicKey,
                GrpcEndpoint = registration.GrpcEndpoint,
                Status = validatorConfig.IsPublicRegistration
                    ? Interfaces.ValidatorStatus.Active
                    : Interfaces.ValidatorStatus.Pending,
                RegisteredAt = now,
                LastStateChangeAt = now,
                OrderIndex = orderIndex,
                Metadata = registration.Metadata
            };

            // Write-through: MongoDB first (durable), then Redis (cache)
            await PersistToMongoAsync(registerId, validator, ct);
            await StoreValidatorAsync(registerId, validator, ct);

            // Update order list
            var newOrder = order.ToList();
            newOrder.Add(registration.ValidatorId);
            await UpdateOrderAsync(registerId, newOrder, ct);

            // Raise event
            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorAdded,
                registration.ValidatorId, currentCount + 1);

            _logger.LogInformation(
                "Validator {ValidatorId} registered for register {RegisterId} (order: {Order})",
                registration.ValidatorId, registerId, orderIndex);

            return ValidatorRegistrationResult.Succeeded(GenerateTransactionId(), orderIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to register validator {ValidatorId} for register {RegisterId}",
                registration.ValidatorId, registerId);
            return ValidatorRegistrationResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(string registerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        _logger.LogInformation("Refreshing validator list for register {RegisterId}", registerId);

        try
        {
            // Clear local cache
            _localCache.TryRemove(registerId, out _);

            // Clear Redis cache
            var validatorsKey = GetValidatorsKey(registerId);
            var orderKey = GetOrderKey(registerId);
            await _database.KeyDeleteAsync([validatorsKey, orderKey]);

            // Cache cleared — validators will be rebuilt from Redis on next access.
            // Chain-based validator discovery can be added when register transactions
            // include validator registration metadata for querying.

            _logger.LogDebug("Validator list cache cleared for register {RegisterId}", registerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh validators for register {RegisterId}", registerId);
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetActiveCountAsync(string registerId, CancellationToken ct = default)
    {
        var validators = await GetActiveValidatorsAsync(registerId, ct);
        return validators.Count;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ValidatorInfo>> GetPendingValidatorsAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        try
        {
            // Get all validators and filter for pending status
            var allValidators = await GetValidatorsFromRedisAsync(registerId, ct);
            return allValidators
                .Where(v => v.Status == Interfaces.ValidatorStatus.Pending)
                .OrderBy(v => v.RegisteredAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending validators for register {RegisterId}", registerId);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<ValidatorApprovalResult> ApproveValidatorAsync(
        string registerId,
        ValidatorApprovalRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _logger.LogInformation(
                "Approving validator {ValidatorId} for register {RegisterId} by {ApprovedBy}",
                request.ValidatorId, registerId, request.ApprovedBy);

            // Check registration mode - approval only makes sense in consent mode
            var validatorConfig = await _genesisConfig.GetValidatorConfigAsync(registerId, ct);
            if (validatorConfig.IsPublicRegistration)
            {
                return ValidatorApprovalResult.Failed(
                    "Register uses public registration mode - approval not required");
            }

            // Get the pending validator
            var validator = await GetValidatorAsync(registerId, request.ValidatorId, ct);
            if (validator == null)
            {
                return ValidatorApprovalResult.Failed("Validator not found");
            }

            if (validator.Status != Interfaces.ValidatorStatus.Pending)
            {
                return ValidatorApprovalResult.Failed(
                    $"Validator is not pending approval (status: {validator.Status})");
            }

            // Check max validators
            var currentCount = await GetActiveCountAsync(registerId, ct);
            if (currentCount >= validatorConfig.MaxValidators)
            {
                return ValidatorApprovalResult.Failed(
                    $"Maximum validators ({validatorConfig.MaxValidators}) reached");
            }

            var approvedAt = DateTimeOffset.UtcNow;

            // Update validator to active status
            var updatedValidator = validator with
            {
                Status = Interfaces.ValidatorStatus.Active,
                Metadata = validator.Metadata == null
                    ? new Dictionary<string, string>
                    {
                        ["approvedBy"] = request.ApprovedBy,
                        ["approvedAt"] = approvedAt.ToString("O"),
                        ["approvalNotes"] = request.ApprovalNotes ?? ""
                    }
                    : new Dictionary<string, string>(validator.Metadata)
                    {
                        ["approvedBy"] = request.ApprovedBy,
                        ["approvedAt"] = approvedAt.ToString("O"),
                        ["approvalNotes"] = request.ApprovalNotes ?? ""
                    }
            };

            // Store updated validator
            await StoreValidatorAsync(registerId, updatedValidator, ct);

            // Clear local cache to pick up changes
            _localCache.TryRemove(registerId, out _);

            // Raise event
            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorApproved,
                request.ValidatorId, currentCount + 1);

            _logger.LogInformation(
                "Validator {ValidatorId} approved for register {RegisterId} (order: {Order})",
                request.ValidatorId, registerId, updatedValidator.OrderIndex);

            // Approval is stored in Redis (source of truth for active validators).
            // On-chain audit of validator approvals is deferred until the
            // ControlDocketProcessor can create properly signed Control transactions.
            return ValidatorApprovalResult.Succeeded(GenerateTransactionId(), updatedValidator.OrderIndex ?? 0, approvedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to approve validator {ValidatorId} for register {RegisterId}",
                request.ValidatorId, registerId);
            return ValidatorApprovalResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RejectValidatorAsync(
        string registerId,
        string validatorId,
        string reason,
        string rejectedBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedBy);

        try
        {
            _logger.LogInformation(
                "Rejecting validator {ValidatorId} for register {RegisterId} by {RejectedBy}: {Reason}",
                validatorId, registerId, rejectedBy, reason);

            // Get the pending validator
            var validator = await GetValidatorAsync(registerId, validatorId, ct);
            if (validator == null)
            {
                _logger.LogWarning("Validator {ValidatorId} not found for rejection", validatorId);
                return false;
            }

            if (validator.Status != Interfaces.ValidatorStatus.Pending)
            {
                _logger.LogWarning(
                    "Validator {ValidatorId} is not pending (status: {Status}), cannot reject",
                    validatorId, validator.Status);
                return false;
            }

            // Update validator to removed status with rejection metadata
            var rejectedValidator = validator with
            {
                Status = Interfaces.ValidatorStatus.Revoked,
                Metadata = validator.Metadata == null
                    ? new Dictionary<string, string>
                    {
                        ["rejectedBy"] = rejectedBy,
                        ["rejectedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["rejectionReason"] = reason
                    }
                    : new Dictionary<string, string>(validator.Metadata)
                    {
                        ["rejectedBy"] = rejectedBy,
                        ["rejectedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["rejectionReason"] = reason
                    }
            };

            // Store updated validator
            await StoreValidatorAsync(registerId, rejectedValidator, ct);

            // Update order list (remove from order)
            var order = await GetValidatorOrderAsync(registerId, ct);
            var newOrder = order.Where(v => v != validatorId).ToList();
            await UpdateOrderAsync(registerId, newOrder, ct);

            // Clear local cache
            _localCache.TryRemove(registerId, out _);

            // Raise event
            var currentCount = await GetActiveCountAsync(registerId, ct);
            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorRejected,
                validatorId, currentCount);

            _logger.LogInformation(
                "Validator {ValidatorId} rejected for register {RegisterId}",
                validatorId, registerId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to reject validator {ValidatorId} for register {RegisterId}",
                validatorId, registerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SuspendValidatorAsync(
        string registerId,
        string validatorId,
        string suspendedBy,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        try
        {
            var validator = await GetValidatorAsync(registerId, validatorId, ct);
            if (validator == null)
            {
                _logger.LogWarning("Cannot suspend: validator {ValidatorId} not found on register {RegisterId}", validatorId, registerId);
                return false;
            }

            if (validator.Status != Interfaces.ValidatorStatus.Active)
            {
                _logger.LogWarning("Cannot suspend: validator {ValidatorId} is {Status}, not Active", validatorId, validator.Status);
                return false;
            }

            // Last-active-validator guard
            var activeCount = await GetActiveCountAsync(registerId, ct);
            if (activeCount <= 1)
            {
                _logger.LogWarning("Cannot suspend: validator {ValidatorId} is the last active validator on register {RegisterId}", validatorId, registerId);
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var suspended = validator with
            {
                Status = Interfaces.ValidatorStatus.Suspended,
                SuspendedAt = now,
                SuspendedBy = suspendedBy,
                LastStateChangeAt = now
            };

            await PersistToMongoAsync(registerId, suspended, ct);
            await StoreValidatorAsync(registerId, suspended, ct);
            _localCache.TryRemove(registerId, out _);

            await WriteAuditEntryAsync(registerId, validatorId,
                Interfaces.ValidatorStatus.Active, Interfaces.ValidatorStatus.Suspended, suspendedBy, reason);

            var newCount = await GetActiveCountAsync(registerId, ct);
            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorSuspended, validatorId, newCount);

            _logger.LogInformation("Validator {ValidatorId} suspended on register {RegisterId} by {SuspendedBy}", validatorId, registerId, suspendedBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to suspend validator {ValidatorId} on register {RegisterId}", validatorId, registerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ReactivateValidatorAsync(
        string registerId,
        string validatorId,
        string reactivatedBy,
        string? notes = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        try
        {
            var validator = await GetValidatorAsync(registerId, validatorId, ct);
            if (validator == null || validator.Status != Interfaces.ValidatorStatus.Suspended)
            {
                _logger.LogWarning("Cannot reactivate: validator {ValidatorId} is not in Suspended state", validatorId);
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            var reactivated = validator with
            {
                Status = Interfaces.ValidatorStatus.Active,
                LastStateChangeAt = now
            };

            await PersistToMongoAsync(registerId, reactivated, ct);
            await StoreValidatorAsync(registerId, reactivated, ct);
            _localCache.TryRemove(registerId, out _);

            await WriteAuditEntryAsync(registerId, validatorId,
                Interfaces.ValidatorStatus.Suspended, Interfaces.ValidatorStatus.Active, reactivatedBy, notes);

            var newCount = await GetActiveCountAsync(registerId, ct);
            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorReactivated, validatorId, newCount);

            _logger.LogInformation("Validator {ValidatorId} reactivated on register {RegisterId} by {ReactivatedBy}", validatorId, registerId, reactivatedBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reactivate validator {ValidatorId} on register {RegisterId}", validatorId, registerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RevokeValidatorAsync(
        string registerId,
        string validatorId,
        string revokedBy,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        try
        {
            var validator = await GetValidatorAsync(registerId, validatorId, ct);
            if (validator == null)
            {
                _logger.LogWarning("Cannot revoke: validator {ValidatorId} not found on register {RegisterId}", validatorId, registerId);
                return false;
            }

            if (validator.Status == Interfaces.ValidatorStatus.Revoked)
            {
                _logger.LogWarning("Cannot revoke: validator {ValidatorId} is already revoked", validatorId);
                return false;
            }

            // Last-active-validator guard (only if currently active)
            if (validator.Status == Interfaces.ValidatorStatus.Active)
            {
                var activeCount = await GetActiveCountAsync(registerId, ct);
                if (activeCount <= 1)
                {
                    _logger.LogWarning("Cannot revoke: validator {ValidatorId} is the last active validator on register {RegisterId}", validatorId, registerId);
                    return false;
                }
            }

            var previousStatus = validator.Status;
            var now = DateTimeOffset.UtcNow;
            var revoked = validator with
            {
                Status = Interfaces.ValidatorStatus.Revoked,
                RevokedAt = now,
                RevokedBy = revokedBy,
                LastStateChangeAt = now
            };

            await PersistToMongoAsync(registerId, revoked, ct);
            await StoreValidatorAsync(registerId, revoked, ct);
            _localCache.TryRemove(registerId, out _);

            // Remove from order list
            var order = await GetValidatorOrderAsync(registerId, ct);
            var newOrder = order.Where(v => v != validatorId).ToList();
            await UpdateOrderAsync(registerId, newOrder, ct);

            await WriteAuditEntryAsync(registerId, validatorId,
                previousStatus, Interfaces.ValidatorStatus.Revoked, revokedBy, reason);

            var newCount = await GetActiveCountAsync(registerId, ct);
            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorRemoved, validatorId, newCount);

            _logger.LogInformation("Validator {ValidatorId} permanently revoked on register {RegisterId} by {RevokedBy}", validatorId, registerId, revokedBy);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke validator {ValidatorId} on register {RegisterId}", validatorId, registerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<Models.ValidatorAuditEntry> Entries, long Total)> GetAuditTrailAsync(
        string registerId,
        string? validatorId = null,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        await EnsureMongoIndexesAsync();

        try
        {
            var filterBuilder = Builders<Models.ValidatorAuditEntry>.Filter;
            var filter = filterBuilder.Eq(a => a.RegisterId, registerId);

            if (!string.IsNullOrWhiteSpace(validatorId))
            {
                filter &= filterBuilder.Eq(a => a.ValidatorId, validatorId);
            }

            var total = await _auditCollection.CountDocumentsAsync(filter, cancellationToken: ct);
            var entries = await _auditCollection
                .Find(filter)
                .SortByDescending(a => a.Timestamp)
                .Skip(offset)
                .Limit(limit)
                .ToListAsync(ct);

            return (entries, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audit trail for register {RegisterId}", registerId);
            return ([], 0);
        }
    }

    /// <inheritdoc/>
    public async Task<int> EnforceRegistrationModeTransitionAsync(
        string registerId,
        IReadOnlyList<ApprovedValidator> approvedValidators,
        TransitionMode mode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(approvedValidators);

        _logger.LogInformation(
            "Enforcing registration mode transition to consent for register {RegisterId} (mode={TransitionMode}, approved={ApprovedCount})",
            registerId, mode, approvedValidators.Count);

        var activeValidators = await GetActiveValidatorsAsync(registerId, ct);
        var approvedSet = new HashSet<string>(
            approvedValidators.SelectMany(v => new[] { v.Did, v.PublicKey }),
            StringComparer.OrdinalIgnoreCase);

        var unapproved = activeValidators
            .Where(v => !approvedSet.Contains(v.ValidatorId) && !approvedSet.Contains(v.PublicKey))
            .ToList();

        if (unapproved.Count == 0)
        {
            _logger.LogInformation(
                "No unapproved validators to eject for register {RegisterId}", registerId);
            return 0;
        }

        _logger.LogWarning(
            "Found {UnapprovedCount} unapproved validators for register {RegisterId} during transition",
            unapproved.Count, registerId);

        var remainingCount = activeValidators.Count;

        foreach (var validator in unapproved)
        {
            remainingCount--;

            switch (mode)
            {
                case TransitionMode.Immediate:
                    // Remove the validator key immediately
                    var key = GetValidatorKey(registerId, validator.ValidatorId);
                    await _database.KeyDeleteAsync(key);

                    _logger.LogInformation(
                        "Ejected unapproved validator {ValidatorId} immediately from register {RegisterId}",
                        validator.ValidatorId, registerId);
                    break;

                case TransitionMode.GracePeriod:
                    // Set a short TTL so the key expires after one cycle instead of being refreshed
                    var graceKey = GetValidatorKey(registerId, validator.ValidatorId);
                    var currentTtl = await _database.KeyTimeToLiveAsync(graceKey);
                    // If already has a TTL, let it expire naturally; otherwise set one cycle
                    if (currentTtl == null || currentTtl < TimeSpan.Zero)
                    {
                        await _database.KeyExpireAsync(graceKey, _config.CacheTtl);
                    }
                    // Mark in metadata that this validator is in grace period (won't be refreshed)
                    var updated = validator with
                    {
                        Metadata = new Dictionary<string, string>(validator.Metadata ?? new())
                        {
                            ["transitionGracePeriod"] = "true",
                            ["gracePeriodStartedAt"] = DateTimeOffset.UtcNow.ToString("O")
                        }
                    };
                    await StoreValidatorAsync(registerId, updated, ct);

                    _logger.LogInformation(
                        "Set grace period for unapproved validator {ValidatorId} in register {RegisterId} (TTL={Ttl})",
                        validator.ValidatorId, registerId, currentTtl ?? _config.CacheTtl);
                    break;
            }

            RaiseValidatorListChanged(registerId, ValidatorListChangeType.ValidatorRemoved,
                validator.ValidatorId, remainingCount);
        }

        // Invalidate list cache
        _localCache.TryRemove(registerId, out _);
        var listKey = GetValidatorsKey(registerId);
        await _database.KeyDeleteAsync(listKey);

        return unapproved.Count;
    }


    private async Task<List<ValidatorInfo>> GetValidatorsFromRedisAsync(
        string registerId,
        CancellationToken ct)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetValidatorsKey(registerId);
                var json = await _database.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                {
                    // Try to build from individual validator keys
                    return await BuildValidatorListAsync(registerId, token);
                }

                return JsonSerializer.Deserialize<List<ValidatorInfo>>(json.ToString(), _jsonOptions) ?? [];
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker open for validator registry {RegisterId}", registerId);
            return [];
        }
    }

    private async Task<List<ValidatorInfo>> BuildValidatorListAsync(
        string registerId,
        CancellationToken ct)
    {
        // Scan for validator keys
        var pattern = $"{_config.KeyPrefix}{registerId}:validator:*";
        var validators = new List<ValidatorInfo>();

        await foreach (var key in _redis.GetServer(_redis.GetEndPoints().First())
            .KeysAsync(pattern: pattern))
        {
            var json = await _database.StringGetAsync(key);
            if (!json.IsNullOrEmpty)
            {
                var validator = JsonSerializer.Deserialize<ValidatorInfo>(json.ToString(), _jsonOptions);
                if (validator != null)
                {
                    validators.Add(validator);
                }
            }
        }

        // Cache the list using operational TTL
        if (validators.Count > 0)
        {
            var listKey = GetValidatorsKey(registerId);
            var ttl = await ResolveOperationalTtlAsync(registerId, ct);
            await _database.StringSetAsync(
                listKey,
                JsonSerializer.Serialize(validators, _jsonOptions),
                ttl);
        }

        return validators;
    }

    private async Task StoreValidatorAsync(
        string registerId,
        ValidatorInfo validator,
        CancellationToken ct,
        TimeSpan? ttlOverride = null)
    {
        var ttl = ttlOverride ?? await ResolveOperationalTtlAsync(registerId, ct);

        await _pipeline.ExecuteAsync(async token =>
        {
            var key = GetValidatorKey(registerId, validator.ValidatorId);
            var json = JsonSerializer.Serialize(validator, _jsonOptions);
            await _database.StringSetAsync(key, json, ttl);

            // Invalidate list cache
            var listKey = GetValidatorsKey(registerId);
            await _database.KeyDeleteAsync(listKey);
        }, ct);
    }

    private async Task UpdateOrderAsync(
        string registerId,
        List<string> order,
        CancellationToken ct)
    {
        await _pipeline.ExecuteAsync(async token =>
        {
            var key = GetOrderKey(registerId);
            var json = JsonSerializer.Serialize(order, _jsonOptions);
            await _database.StringSetAsync(key, json, _config.CacheTtl);
        }, ct);
    }

    private bool TryGetFromLocalCache(
        string registerId,
        out List<ValidatorInfo>? validators)
    {
        validators = null;

        if (!_localCache.TryGetValue(registerId, out var entry))
            return false;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _localCache.TryRemove(registerId, out _);
            return false;
        }

        validators = entry.Validators;
        return true;
    }

    private void SetInLocalCache(string registerId, List<ValidatorInfo> validators)
    {
        // Enforce max entries
        if (_localCache.Count >= _config.LocalCacheMaxEntries)
        {
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

        _localCache[registerId] = new LocalCacheEntry
        {
            Validators = validators,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_config.LocalCacheTtl)
        };
    }

    private void RaiseValidatorListChanged(
        string registerId,
        ValidatorListChangeType changeType,
        string validatorId,
        int newCount)
    {
        var args = new ValidatorListChangedEventArgs
        {
            RegisterId = registerId,
            ChangeType = changeType,
            ValidatorId = validatorId,
            NewValidatorCount = newCount
        };

        ValidatorListChanged?.Invoke(this, args);
    }


    /// <summary>
    /// Resolves the operational TTL for a register from its policy, with fallback to config default.
    /// Caches the result for 60 seconds to avoid repeated policy lookups.
    /// </summary>
    internal async Task<TimeSpan> ResolveOperationalTtlAsync(string registerId, CancellationToken ct = default)
    {
        // Check TTL cache (valid for 60 seconds)
        if (_operationalTtlCache.TryGetValue(registerId, out var cached)
            && cached.CachedAt.AddSeconds(60) > DateTimeOffset.UtcNow)
        {
            return cached.Ttl;
        }

        try
        {
            var policyResponse = await _registerClient.GetRegisterPolicyAsync(registerId, ct);
            var ttlSeconds = policyResponse?.Policy?.Validators?.OperationalTtlSeconds
                ?? _config.DefaultOperationalTtlSeconds;

            var ttl = TimeSpan.FromSeconds(ttlSeconds);
            _operationalTtlCache[registerId] = (ttl, DateTimeOffset.UtcNow);

            _logger.LogDebug(
                "Resolved operational TTL for register {RegisterId}: {TtlSeconds}s",
                registerId, ttlSeconds);

            return ttl;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve operational TTL for register {RegisterId}, using default {DefaultTtl}s",
                registerId, _config.DefaultOperationalTtlSeconds);

            var fallback = TimeSpan.FromSeconds(_config.DefaultOperationalTtlSeconds);
            _operationalTtlCache[registerId] = (fallback, DateTimeOffset.UtcNow);
            return fallback;
        }
    }

    /// <summary>
    /// Generates a 64-character hex transaction ID (SHA-256 hash).
    /// </summary>
    private static string GenerateTransactionId()
    {
        var input = Encoding.UTF8.GetBytes($"{Guid.NewGuid()}{DateTimeOffset.UtcNow.Ticks}");
        var hash = SHA256.HashData(input);
        return Convert.ToHexStringLower(hash);
    }

    // ===========================
    // MongoDB Durable Persistence
    // ===========================

    /// <summary>
    /// Ensures MongoDB indexes are created (idempotent, called once per startup).
    /// </summary>
    private async Task EnsureMongoIndexesAsync()
    {
        if (_mongoIndexesCreated) return;

        await _indexCreationLock.WaitAsync();
        try
        {
            if (_mongoIndexesCreated) return; // Double-check after acquiring lock
            var validatorIndexes = new List<CreateIndexModel<ValidatorDocument>>
            {
                new(Builders<ValidatorDocument>.IndexKeys
                        .Ascending(d => d.RegisterId)
                        .Ascending(d => d.Status)),
                new(Builders<ValidatorDocument>.IndexKeys
                        .Ascending(d => d.RegisterId)
                        .Ascending(d => d.ValidatorId),
                    new CreateIndexOptions { Unique = true })
            };
            await _validatorsCollection.Indexes.CreateManyAsync(validatorIndexes);

            var auditIndexes = new List<CreateIndexModel<ValidatorAuditEntry>>
            {
                new(Builders<ValidatorAuditEntry>.IndexKeys
                        .Ascending(a => a.RegisterId)
                        .Ascending(a => a.ValidatorId)
                        .Descending(a => a.Timestamp)),
                new(Builders<ValidatorAuditEntry>.IndexKeys
                        .Descending(a => a.Timestamp))
            };
            await _auditCollection.Indexes.CreateManyAsync(auditIndexes);

            _mongoIndexesCreated = true;
            _logger.LogInformation("MongoDB indexes created for validator registry");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create MongoDB indexes — will retry on next operation");
        }
        finally
        {
            _indexCreationLock.Release();
        }
    }

    /// <summary>
    /// Persists a validator to MongoDB (write-through from Redis).
    /// </summary>
    private async Task PersistToMongoAsync(string registerId, ValidatorInfo validator, CancellationToken ct = default)
    {
        await EnsureMongoIndexesAsync();

        try
        {
            var doc = ValidatorDocument.FromValidatorInfo(registerId, validator);
            var filter = Builders<ValidatorDocument>.Filter.And(
                Builders<ValidatorDocument>.Filter.Eq(d => d.RegisterId, registerId),
                Builders<ValidatorDocument>.Filter.Eq(d => d.ValidatorId, validator.ValidatorId));

            await _validatorsCollection.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist validator {ValidatorId} to MongoDB for register {RegisterId}",
                validator.ValidatorId, registerId);
        }
    }

    /// <summary>
    /// Writes an audit entry to MongoDB.
    /// </summary>
    private async Task WriteAuditEntryAsync(
        string registerId,
        string validatorId,
        Interfaces.ValidatorStatus previousStatus,
        Interfaces.ValidatorStatus newStatus,
        string performedBy,
        string? reason = null)
    {
        await EnsureMongoIndexesAsync();

        try
        {
            var entry = new ValidatorAuditEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                RegisterId = registerId,
                ValidatorId = validatorId,
                PreviousStatus = previousStatus,
                NewStatus = newStatus,
                PerformedBy = performedBy,
                Reason = reason,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _auditCollection.InsertOneAsync(entry);

            _logger.LogInformation(
                "Audit: Validator {ValidatorId} on register {RegisterId}: {PreviousStatus} → {NewStatus} by {PerformedBy}",
                validatorId, registerId, previousStatus, newStatus, performedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit entry for validator {ValidatorId}", validatorId);
        }
    }

    /// <summary>
    /// Hydrates Redis cache from MongoDB on startup.
    /// Call during service initialization to ensure durable state survives Redis restarts.
    /// </summary>
    public async Task HydrateFromMongoAsync(CancellationToken ct = default)
    {
        await EnsureMongoIndexesAsync();

        try
        {
            var cursor = await _validatorsCollection.FindAsync(
                Builders<ValidatorDocument>.Filter.Empty,
                cancellationToken: ct);

            var validators = await cursor.ToListAsync(ct);
            var byRegister = validators.GroupBy(v => v.RegisterId);

            foreach (var group in byRegister)
            {
                var registerId = group.Key;
                foreach (var doc in group)
                {
                    var validatorInfo = doc.ToValidatorInfo();
                    await StoreValidatorAsync(registerId, validatorInfo, ct);
                }

                _logger.LogInformation(
                    "Hydrated {Count} validators from MongoDB for register {RegisterId}",
                    group.Count(), registerId);
            }

            _logger.LogInformation("MongoDB hydration complete: {Total} validators loaded", validators.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hydrate validator registry from MongoDB");
        }
    }

    private class LocalCacheEntry
    {
        public required List<ValidatorInfo> Validators { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
