// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Resolves control blueprint versions by tracking the control transaction chain.
/// Version 1 is always from the genesis block; subsequent versions come from control updates.
/// </summary>
public class ControlBlueprintVersionResolver : IControlBlueprintVersionResolver
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly IGenesisConfigService _genesisConfigService;
    private readonly ILogger<ControlBlueprintVersionResolver> _logger;

    // Cache of version history per register
    private readonly ConcurrentDictionary<string, List<ControlBlueprintVersionInfo>> _versionHistoryCache = new();
    private readonly ConcurrentDictionary<string, ResolvedControlBlueprintVersion> _activeVersionCache = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cacheTimestamps = new();

    // Cache expiration
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    // Control action IDs
    private const string ConfigUpdateActionId = "control.config.update";
    private const string ValidatorRegisterActionId = "control.validator.register";
    private const string ValidatorApproveActionId = "control.validator.approve";
    private const string ValidatorSuspendActionId = "control.validator.suspend";
    private const string ValidatorRemoveActionId = "control.validator.remove";

    /// <inheritdoc/>
    public event EventHandler<ControlBlueprintVersionChangedEventArgs>? VersionChanged;

    public ControlBlueprintVersionResolver(
        IRegisterServiceClient registerClient,
        IGenesisConfigService genesisConfigService,
        ILogger<ControlBlueprintVersionResolver> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _genesisConfigService = genesisConfigService ?? throw new ArgumentNullException(nameof(genesisConfigService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ResolvedControlBlueprintVersion?> GetActiveVersionAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        // Check cache first
        if (TryGetCachedActiveVersion(registerId, out var cached))
        {
            return cached;
        }

        _logger.LogDebug("Resolving active control blueprint version for register {RegisterId}", registerId);

        try
        {
            // Get version history and return the latest
            var history = await GetVersionHistoryAsync(registerId, ct);
            var latestInfo = history.LastOrDefault();

            if (latestInfo == null)
            {
                // No version history - try to get from genesis config
                var genesisConfig = await _genesisConfigService.GetFullConfigAsync(registerId, ct);
                return CreateGenesisVersion(registerId, genesisConfig);
            }

            // Build full resolved version
            var resolved = await BuildResolvedVersionAsync(registerId, latestInfo, ct);

            // Cache the result
            if (resolved != null)
            {
                _activeVersionCache[registerId] = resolved;
                _cacheTimestamps[registerId] = DateTimeOffset.UtcNow;
            }

            return resolved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve active control blueprint version for register {RegisterId}", registerId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ResolvedControlBlueprintVersion?> GetByTransactionAsync(
        string registerId,
        string controlTransactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlTransactionId);

        _logger.LogDebug(
            "Resolving control blueprint version from transaction {TxId} for register {RegisterId}",
            controlTransactionId, registerId);

        try
        {
            var history = await GetVersionHistoryAsync(registerId, ct);
            var versionInfo = history.FirstOrDefault(v => v.TransactionId == controlTransactionId);

            if (versionInfo == null)
            {
                _logger.LogWarning(
                    "Control transaction {TxId} not found in version history for register {RegisterId}",
                    controlTransactionId, registerId);
                return null;
            }

            return await BuildResolvedVersionAsync(registerId, versionInfo, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve control blueprint version from transaction {TxId} for register {RegisterId}",
                controlTransactionId, registerId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ControlBlueprintVersionInfo>> GetVersionHistoryAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        // Check cache first
        if (TryGetCachedHistory(registerId, out var cached))
        {
            return cached!.AsReadOnly();
        }

        _logger.LogDebug("Building control blueprint version history for register {RegisterId}", registerId);

        var versions = new List<ControlBlueprintVersionInfo>();

        try
        {
            // Version 1: Genesis
            var genesisDocket = await _registerClient.ReadDocketAsync(registerId, 0, ct);
            if (genesisDocket != null)
            {
                var genesisTransaction = genesisDocket.Transactions.FirstOrDefault();
                if (genesisTransaction != null)
                {
                    versions.Add(new ControlBlueprintVersionInfo
                    {
                        RegisterId = registerId,
                        VersionNumber = 1,
                        TransactionId = genesisTransaction.TxId ?? genesisTransaction.Id ?? "genesis",
                        ActiveFrom = new DateTimeOffset(genesisTransaction.TimeStamp, TimeSpan.Zero),
                        ChangeType = "Genesis",
                        ChangeDescription = "Initial control blueprint from genesis block",
                        IsActive = false // Will be updated later
                    });
                }
            }

            // Scan for control update transactions
            var controlTransactions = await FindControlTransactionsAsync(registerId, ct);

            foreach (var tx in controlTransactions.OrderBy(t => t.TimeStamp))
            {
                var changeType = DetermineChangeType(tx);
                var changeDescription = DetermineChangeDescription(tx);

                versions.Add(new ControlBlueprintVersionInfo
                {
                    RegisterId = registerId,
                    VersionNumber = versions.Count + 1,
                    TransactionId = tx.TxId ?? tx.Id ?? string.Empty,
                    ActiveFrom = new DateTimeOffset(tx.TimeStamp, TimeSpan.Zero),
                    ChangeType = changeType,
                    ChangeDescription = changeDescription,
                    IsActive = false
                });
            }

            // Mark the latest as active
            if (versions.Count > 0)
            {
                versions[^1] = versions[^1] with { IsActive = true };
            }

            // Cache the result
            _versionHistoryCache[registerId] = versions;
            _cacheTimestamps[registerId] = DateTimeOffset.UtcNow;

            return versions.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build version history for register {RegisterId}", registerId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ResolvedControlBlueprintVersion?> GetVersionAsOfAsync(
        string registerId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        _logger.LogDebug(
            "Resolving control blueprint version as of {AsOf} for register {RegisterId}",
            asOf, registerId);

        var history = await GetVersionHistoryAsync(registerId, ct);

        // Find the latest version that was active before or at the specified time
        var versionAtTime = history
            .Where(v => v.ActiveFrom <= asOf)
            .OrderByDescending(v => v.ActiveFrom)
            .FirstOrDefault();

        if (versionAtTime == null)
        {
            _logger.LogWarning(
                "No control blueprint version found as of {AsOf} for register {RegisterId}",
                asOf, registerId);
            return null;
        }

        return await BuildResolvedVersionAsync(registerId, versionAtTime, ct);
    }

    /// <inheritdoc/>
    public async Task<bool> HasPendingUpdatesAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        // Check if there are any control transactions in the mempool
        // This would require access to the mempool manager
        // For now, return false as we don't have pending docket tracking
        _logger.LogDebug("Checking for pending control updates in register {RegisterId}", registerId);

        await Task.CompletedTask; // Placeholder for future implementation
        return false;
    }

    /// <inheritdoc/>
    public void InvalidateCache(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        _logger.LogDebug("Invalidating control blueprint version cache for register {RegisterId}", registerId);

        _versionHistoryCache.TryRemove(registerId, out _);
        _activeVersionCache.TryRemove(registerId, out _);
        _cacheTimestamps.TryRemove(registerId, out _);
    }


    private bool TryGetCachedActiveVersion(string registerId, out ResolvedControlBlueprintVersion? cached)
    {
        cached = null;

        if (!_activeVersionCache.TryGetValue(registerId, out var cachedVersion))
            return false;

        if (!_cacheTimestamps.TryGetValue(registerId, out var timestamp))
            return false;

        if (DateTimeOffset.UtcNow - timestamp > CacheExpiration)
        {
            // Cache expired
            _activeVersionCache.TryRemove(registerId, out _);
            return false;
        }

        cached = cachedVersion;
        return true;
    }

    private bool TryGetCachedHistory(string registerId, out List<ControlBlueprintVersionInfo>? cached)
    {
        cached = null;

        if (!_versionHistoryCache.TryGetValue(registerId, out var cachedHistory))
            return false;

        if (!_cacheTimestamps.TryGetValue(registerId, out var timestamp))
            return false;

        if (DateTimeOffset.UtcNow - timestamp > CacheExpiration)
        {
            // Cache expired
            _versionHistoryCache.TryRemove(registerId, out _);
            return false;
        }

        cached = cachedHistory;
        return true;
    }

    private ResolvedControlBlueprintVersion CreateGenesisVersion(
        string registerId,
        GenesisConfiguration config)
    {
        return new ResolvedControlBlueprintVersion
        {
            RegisterId = registerId,
            VersionNumber = 1,
            TransactionId = config.GenesisTransactionId,
            ActiveFrom = config.LoadedAt,
            Configuration = config,
            IsActive = true,
            PreviousVersionTransactionId = null,
            ChangeDescription = "Initial control blueprint from genesis block"
        };
    }

    private async Task<ResolvedControlBlueprintVersion?> BuildResolvedVersionAsync(
        string registerId,
        ControlBlueprintVersionInfo versionInfo,
        CancellationToken ct)
    {
        // Get the configuration that was active at this version
        // For now, we use the current genesis config and apply changes up to this version
        // A more complete implementation would reconstruct the config at each version

        var config = await _genesisConfigService.GetFullConfigAsync(registerId, ct);

        // If this is the genesis version, use the config as-is
        if (versionInfo.VersionNumber == 1)
        {
            return new ResolvedControlBlueprintVersion
            {
                RegisterId = registerId,
                VersionNumber = 1,
                TransactionId = versionInfo.TransactionId,
                ActiveFrom = versionInfo.ActiveFrom,
                Configuration = config,
                IsActive = versionInfo.IsActive,
                PreviousVersionTransactionId = null,
                ChangeDescription = versionInfo.ChangeDescription
            };
        }

        // For later versions, we would need to reconstruct the config state
        // For now, we return the current config with version metadata
        // TODO: Implement proper config reconstruction by replaying control transactions

        var history = await GetVersionHistoryAsync(registerId, ct);
        var previousVersion = history.FirstOrDefault(v => v.VersionNumber == versionInfo.VersionNumber - 1);

        return new ResolvedControlBlueprintVersion
        {
            RegisterId = registerId,
            VersionNumber = versionInfo.VersionNumber,
            TransactionId = versionInfo.TransactionId,
            ActiveFrom = versionInfo.ActiveFrom,
            Configuration = config, // Current config - TODO: reconstruct historical state
            IsActive = versionInfo.IsActive,
            PreviousVersionTransactionId = previousVersion?.TransactionId,
            ChangeDescription = versionInfo.ChangeDescription
        };
    }

    private async Task<List<TransactionModel>> FindControlTransactionsAsync(
        string registerId,
        CancellationToken ct)
    {
        var controlTransactions = new List<TransactionModel>();

        try
        {
            // Get transactions and filter for control actions
            var page = 1;
            var pageSize = 100;
            var hasMore = true;

            while (hasMore)
            {
                var txPage = await _registerClient.GetTransactionsAsync(registerId, page, pageSize, ct);

                foreach (var tx in txPage.Transactions)
                {
                    if (IsControlTransaction(tx))
                    {
                        controlTransactions.Add(tx);
                    }
                }

                hasMore = page < txPage.TotalPages;
                page++;

                // Safety limit
                if (page > 100)
                {
                    _logger.LogWarning("Exceeded maximum page limit while scanning for control transactions");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for control transactions in register {RegisterId}", registerId);
        }

        return controlTransactions;
    }

    private static bool IsControlTransaction(TransactionModel tx)
    {
        if (tx.MetaData == null)
            return false;

        // Check if the ActionId indicates a control action
        var actionId = tx.MetaData.ActionId?.ToString();

        if (string.IsNullOrEmpty(actionId))
            return false;

        // Control transactions have action IDs starting with "control."
        return actionId.StartsWith("control.", StringComparison.OrdinalIgnoreCase) ||
               IsControlActionId(actionId);
    }

    private static bool IsControlActionId(string? actionId)
    {
        if (string.IsNullOrEmpty(actionId))
            return false;

        return actionId == ConfigUpdateActionId ||
               actionId == ValidatorRegisterActionId ||
               actionId == ValidatorApproveActionId ||
               actionId == ValidatorSuspendActionId ||
               actionId == ValidatorRemoveActionId;
    }

    private static string DetermineChangeType(TransactionModel tx)
    {
        var actionId = tx.MetaData?.ActionId?.ToString();

        return actionId switch
        {
            ConfigUpdateActionId => "ConfigUpdate",
            ValidatorRegisterActionId => "ValidatorRegistration",
            ValidatorApproveActionId => "ValidatorApproval",
            ValidatorSuspendActionId => "ValidatorSuspension",
            ValidatorRemoveActionId => "ValidatorRemoval",
            _ when actionId?.StartsWith("control.") == true => "ControlAction",
            _ => "Unknown"
        };
    }

    private static string? DetermineChangeDescription(TransactionModel tx)
    {
        var changeType = DetermineChangeType(tx);

        // Try to extract description from payload
        if (tx.Payloads != null && tx.Payloads.Length > 0)
        {
            try
            {
                var payloadData = tx.Payloads[0].Data;
                if (!string.IsNullOrEmpty(payloadData))
                {
                    var payload = JsonSerializer.Deserialize<JsonElement>(payloadData);

                    if (payload.TryGetProperty("reason", out var reason))
                    {
                        return reason.GetString();
                    }

                    if (payload.TryGetProperty("description", out var description))
                    {
                        return description.GetString();
                    }
                }
            }
            catch
            {
                // Ignore deserialization errors
            }
        }

        return changeType switch
        {
            "ConfigUpdate" => "Configuration update",
            "ValidatorRegistration" => "Validator registered",
            "ValidatorApproval" => "Validator approved",
            "ValidatorSuspension" => "Validator suspended",
            "ValidatorRemoval" => "Validator removed",
            _ => null
        };
    }

    /// <summary>
    /// Called when a control transaction is committed to raise the version changed event
    /// </summary>
    internal void OnVersionChanged(
        string registerId,
        int previousVersion,
        int newVersion,
        string transactionId,
        string changeType)
    {
        InvalidateCache(registerId);

        VersionChanged?.Invoke(this, new ControlBlueprintVersionChangedEventArgs
        {
            RegisterId = registerId,
            PreviousVersionNumber = previousVersion,
            NewVersionNumber = newVersion,
            TransactionId = transactionId,
            ChangeType = changeType,
            ChangedAt = DateTimeOffset.UtcNow
        });

        _logger.LogInformation(
            "Control blueprint version changed for register {RegisterId}: v{Previous} -> v{New} ({ChangeType})",
            registerId, previousVersion, newVersion, changeType);
    }

}
