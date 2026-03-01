// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Encryption;

/// <summary>
/// Groups recipients by identical disclosure field sets to minimize encryption operations.
/// Recipients sharing the exact same disclosed fields are grouped together so the payload
/// is encrypted once per unique field set rather than once per recipient.
/// </summary>
public sealed class DisclosureGroupBuilder : IDisclosureGroupBuilder
{
    /// <inheritdoc />
    public DisclosureGroup[] BuildGroups(
        Dictionary<string, Dictionary<string, object>> disclosedPayloads,
        RecipientInfo[] recipients)
    {
        if (disclosedPayloads is null || disclosedPayloads.Count == 0)
        {
            return [];
        }

        // Build a lookup of wallet address → RecipientInfo for fast matching
        var recipientLookup = new Dictionary<string, RecipientInfo>(StringComparer.Ordinal);
        foreach (var recipient in recipients)
        {
            recipientLookup[recipient.WalletAddress] = recipient;
        }

        // Group wallets by their sorted field set (represented as GroupId)
        var groupMap = new Dictionary<string, (string[] SortedFields, Dictionary<string, object> Payload, List<RecipientInfo> Recipients)>(StringComparer.Ordinal);

        foreach (var (walletAddress, payload) in disclosedPayloads)
        {
            // Skip wallets that have no matching RecipientInfo
            if (!recipientLookup.TryGetValue(walletAddress, out var recipientInfo))
            {
                continue;
            }

            // Extract sorted field names
            var sortedFields = payload.Keys.OrderBy(k => k).ToArray();

            // Compute deterministic group ID: SHA-256 hex of joined sorted field names
            var groupId = ComputeGroupId(sortedFields);

            if (groupMap.TryGetValue(groupId, out var existing))
            {
                existing.Recipients.Add(recipientInfo);
            }
            else
            {
                groupMap[groupId] = (sortedFields, payload, new List<RecipientInfo> { recipientInfo });
            }
        }

        // Build result array sorted by GroupId for determinism
        return groupMap
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new DisclosureGroup
            {
                GroupId = kvp.Key,
                DisclosedFields = kvp.Value.SortedFields,
                FilteredPayload = kvp.Value.Payload,
                Recipients = kvp.Value.Recipients.ToArray()
            })
            .ToArray();
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hex hash of the sorted field names joined by "|".
    /// </summary>
    private static string ComputeGroupId(string[] sortedFields)
    {
        var joined = string.Join("|", sortedFields);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
