// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Core.Managers;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Extracts the active crypto policy from a register's control transaction chain.
/// The latest version wins — policy updates are delivered via control transactions.
/// </summary>
public class CryptoPolicyService
{
    private readonly TransactionManager _transactionManager;
    private readonly ILogger<CryptoPolicyService> _logger;

    public CryptoPolicyService(
        TransactionManager transactionManager,
        ILogger<CryptoPolicyService> logger)
    {
        _transactionManager = transactionManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets the active crypto policy for a register by scanning its control transaction chain.
    /// Returns the default policy if no explicit policy has been set.
    /// </summary>
    public async Task<CryptoPolicy> GetActivePolicyAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get genesis transaction to check for embedded policy
            var genesisPolicy = await ExtractGenesisPolicyAsync(registerId, cancellationToken);

            // Scan control transactions for policy updates (latest version wins)
            var latestUpdate = await FindLatestPolicyUpdateAsync(registerId, cancellationToken);

            if (latestUpdate != null)
                return latestUpdate;

            if (genesisPolicy != null)
                return genesisPolicy;

            // No explicit policy — return default (permissive, all algorithms accepted)
            _logger.LogDebug("No crypto policy found for register {RegisterId}, using default", registerId);
            return CryptoPolicy.CreateDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract crypto policy for register {RegisterId}, using default", registerId);
            return CryptoPolicy.CreateDefault();
        }
    }

    /// <summary>
    /// Gets all policy versions for a register, ordered by version number.
    /// </summary>
    public async Task<List<CryptoPolicy>> GetPolicyHistoryAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        var policies = new List<CryptoPolicy>();

        var genesisPolicy = await ExtractGenesisPolicyAsync(registerId, cancellationToken);
        if (genesisPolicy != null)
            policies.Add(genesisPolicy);

        var updates = await FindAllPolicyUpdatesAsync(registerId, cancellationToken);
        policies.AddRange(updates);

        return policies.OrderBy(p => p.Version).ToList();
    }

    private async Task<CryptoPolicy?> ExtractGenesisPolicyAsync(
        string registerId, CancellationToken ct)
    {
        // Genesis is the first control transaction on the register — pushed down to the store
        // (earliest Control by TimeStamp) instead of materialising the whole ledger.
        var earliestControl = await _transactionManager.GetTransactionsByTypeAsync(
            registerId, TransactionType.Control, TransactionSort.TimeStampAscending, skip: 0, take: 1, cancellationToken: ct);
        var genesis = earliestControl.FirstOrDefault();

        if (genesis?.Payloads == null || genesis.Payloads.Length == 0)
            return null;

        return ExtractPolicyFromPayload(genesis.Payloads[0].Data);
    }

    private async Task<CryptoPolicy?> FindLatestPolicyUpdateAsync(
        string registerId, CancellationToken ct)
    {
        var updates = await FindAllPolicyUpdatesAsync(registerId, ct);
        return updates.OrderByDescending(p => p.Version).FirstOrDefault();
    }

    private async Task<List<CryptoPolicy>> FindAllPolicyUpdatesAsync(
        string registerId, CancellationToken ct)
    {
        var policies = new List<CryptoPolicy>();

        // Pushed down to the store: all Control txs, earliest first (index-backed), then the
        // policy-update filter in memory over that small subset.
        var allControl = await _transactionManager.GetTransactionsByTypeAsync(
            registerId, TransactionType.Control, TransactionSort.TimeStampAscending, cancellationToken: ct);
        var controlTxs = allControl.Where(IsCryptoPolicyUpdate);

        foreach (var tx in controlTxs)
        {
            if (tx.Payloads == null || tx.Payloads.Length == 0)
                continue;

            var policy = ExtractPolicyFromPayload(tx.Payloads[0].Data);
            if (policy != null)
                policies.Add(policy);
        }

        return policies;
    }

    private static bool IsCryptoPolicyUpdate(TransactionModel tx)
    {
        // Check metadata for crypto policy update marker
        if (tx.MetaData?.TrackingData != null &&
            tx.MetaData.TrackingData.TryGetValue("transactionType", out var txType) &&
            txType == "CryptoPolicyUpdate")
        {
            return true;
        }

        return false;
    }

    private static CryptoPolicy? ExtractPolicyFromPayload(string? base64Data)
    {
        if (string.IsNullOrEmpty(base64Data))
            return null;

        try
        {
            // Payload data is base64url-encoded JSON
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64Data);
            }
            catch (FormatException)
            {
                // Try base64url decoding
                var padded = base64Data.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                bytes = Convert.FromBase64String(padded);
            }

            var json = System.Text.Encoding.UTF8.GetString(bytes);

            // Try to deserialize as RegisterControlRecord (genesis) or CryptoPolicy (update)
            // First, try as a CryptoPolicy directly
            var policy = JsonSerializer.Deserialize<CryptoPolicy>(json);
            if (policy != null && policy.Version > 0 && policy.AcceptedSignatureAlgorithms.Length > 0)
                return policy;

            // Try as RegisterControlRecord (either bare-shape pre-#868 or wrapped post-#868
            // — RegisterControlPayloadReader handles both) and extract its CryptoPolicy.
            var controlRecord = RegisterControlPayloadReader.TryRead(bytes);
            return controlRecord?.CryptoPolicy;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
