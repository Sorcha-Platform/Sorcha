// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Core.Services;

/// <summary>
/// Resolves the effective operational policy for a register by reading the control chain.
/// </summary>
public interface IRegisterPolicyService
{
    /// <summary>
    /// Gets the effective (latest) policy for a register. If no policy has been explicitly set
    /// on the control record, returns <see cref="RegisterPolicy.CreateDefault()"/>.
    /// </summary>
    /// <param name="registerId">Register ID to resolve policy for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The effective register policy</returns>
    Task<RegisterPolicy> GetEffectivePolicyAsync(string registerId, CancellationToken ct = default);

    /// <summary>
    /// Gets a paginated history of policy snapshots extracted from the control transaction chain.
    /// Each entry represents the policy state at the time of that control transaction.
    /// </summary>
    /// <param name="registerId">Register ID to query policy history for</param>
    /// <param name="page">Page number (1-based)</param>
    /// <param name="pageSize">Number of items per page</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tuple of (paginated policies, total count across all pages)</returns>
    Task<(List<RegisterPolicy> Policies, int TotalCount)> GetPolicyHistoryAsync(string registerId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Validates that a blueprint version exists in the system register.
    /// Used to reject governance proposals that reference non-existent blueprint versions.
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier to validate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the blueprint exists in the system register</returns>
    Task<bool> ValidateBlueprintVersionExistsAsync(string blueprintId, CancellationToken ct = default);
}

/// <summary>
/// Validates blueprint existence in the system register.
/// Implemented in the Register Service layer where <c>SystemRegisterService</c> is available.
/// </summary>
public interface ISystemBlueprintValidator
{
    /// <summary>
    /// Checks whether a blueprint exists in the system register.
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if the blueprint exists and is active</returns>
    Task<bool> ExistsAsync(string blueprintId, CancellationToken ct = default);
}

/// <summary>
/// Resolves register operational policy from the control transaction chain via direct repository access.
/// Falls back to <see cref="RegisterPolicy.CreateDefault()"/> when no explicit policy is found.
/// </summary>
public class RegisterPolicyService : IRegisterPolicyService
{
    private static readonly JsonSerializerOptions s_deserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyRegisterRepository _repository;
    private readonly ISystemBlueprintValidator _blueprintValidator;
    private readonly ILogger<RegisterPolicyService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegisterPolicyService"/> class.
    /// </summary>
    /// <param name="repository">Register repository for querying control transactions directly</param>
    /// <param name="blueprintValidator">System blueprint validator for governance proposal checks</param>
    /// <param name="logger">Logger instance</param>
    public RegisterPolicyService(
        IReadOnlyRegisterRepository repository,
        ISystemBlueprintValidator blueprintValidator,
        ILogger<RegisterPolicyService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _blueprintValidator = blueprintValidator ?? throw new ArgumentNullException(nameof(blueprintValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<RegisterPolicy> GetEffectivePolicyAsync(string registerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        _logger.LogDebug("Resolving effective policy for register {RegisterId}", registerId);

        var controlTxs = await _repository.QueryTransactionsAsync(
            registerId,
            t => t.MetaData != null && t.MetaData.TransactionType == TransactionType.Control,
            ct);

        var latestControlTx = controlTxs
            .OrderByDescending(t => t.DocketNumber ?? 0)
            .FirstOrDefault();

        if (latestControlTx is null)
        {
            _logger.LogWarning(
                "No control transactions found for register {RegisterId}, returning default policy v{Version}",
                registerId, 1);
            return RegisterPolicy.CreateDefault();
        }

        var payload = DeserializeControlPayload(latestControlTx);

        if (payload?.Roster?.RegisterPolicy is not null)
        {
            _logger.LogInformation(
                "Resolved explicit policy v{Version} for register {RegisterId} from TX {TxId}",
                payload.Roster.RegisterPolicy.Version, registerId, latestControlTx.TxId);
            return payload.Roster.RegisterPolicy;
        }

        _logger.LogInformation(
            "No explicit policy on control record for register {RegisterId}, returning default policy v{Version}",
            registerId, 1);
        return RegisterPolicy.CreateDefault();
    }

    /// <inheritdoc/>
    public async Task<(List<RegisterPolicy> Policies, int TotalCount)> GetPolicyHistoryAsync(
        string registerId, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);

        _logger.LogDebug(
            "Retrieving policy history for register {RegisterId} (page {Page}, size {PageSize})",
            registerId, page, pageSize);

        var controlTxs = await _repository.QueryTransactionsAsync(
            registerId,
            t => t.MetaData != null && t.MetaData.TransactionType == TransactionType.Control,
            ct);

        var ordered = controlTxs
            .OrderByDescending(t => t.DocketNumber ?? 0)
            .ToList();

        var totalCount = ordered.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var policies = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(tx =>
            {
                var payload = DeserializeControlPayload(tx);
                return payload?.Roster?.RegisterPolicy ?? RegisterPolicy.CreateDefault();
            })
            .ToList();

        _logger.LogInformation(
            "Retrieved {Count} policy snapshots for register {RegisterId} (page {Page}/{TotalPages})",
            policies.Count, registerId, page, totalPages);

        return (policies, totalCount);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateBlueprintVersionExistsAsync(string blueprintId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        _logger.LogDebug("Validating blueprint version exists: {BlueprintId}", blueprintId);

        var exists = await _blueprintValidator.ExistsAsync(blueprintId, ct);

        if (!exists)
        {
            _logger.LogWarning(
                "Blueprint version '{BlueprintId}' not found in system register — governance proposal should be rejected",
                blueprintId);
        }

        return exists;
    }

    private ControlTransactionPayload? DeserializeControlPayload(TransactionModel transaction)
    {
        try
        {
            if (transaction.Payloads == null || transaction.Payloads.Length == 0)
                return null;

            var payloadData = transaction.Payloads[0].Data;
            if (string.IsNullOrWhiteSpace(payloadData))
                return null;

            // Smart decode: legacy Base64 (+, /, =) or Base64url
            var payloadBytes = payloadData.Contains('+') || payloadData.Contains('/') || payloadData.Contains('=')
                ? Convert.FromBase64String(payloadData)
                : System.Buffers.Text.Base64Url.DecodeFromChars(payloadData);

            return JsonSerializer.Deserialize<ControlTransactionPayload>(payloadBytes, s_deserializeOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize control transaction payload for TX {TxId}", transaction.TxId);
            return null;
        }
    }
}
