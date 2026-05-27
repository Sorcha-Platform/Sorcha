// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.TransactionHandler.Services;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Extracts the <see cref="RegisterControlRecord"/> from a genesis docket's
/// transactions. Used by the docket-write path to create a register row when a
/// peer replicates a register's genesis (docket 0) that this node has not yet
/// created locally — the "create-on-sync" path that lets a subscribed node hold
/// a fully-replicated copy of a register it does not own.
/// </summary>
internal static class GenesisControlRecordExtractor
{
    /// <summary>
    /// Returns the first Control transaction's <see cref="RegisterControlRecord"/>
    /// from the supplied transactions, or null if none is present / decodable.
    /// </summary>
    /// <remarks>
    /// Payload bytes are <c>base64url</c>/<c>base64</c>-encoded canonical JSON (the
    /// same wire shape the participant index and crypto-policy paths decode). The
    /// <c>[JsonPropertyName]</c> attributes on <see cref="RegisterControlRecord"/>
    /// make deserialisation naming-policy independent, matching
    /// <c>GenesisIngestionService</c> and <c>CryptoPolicyService</c>.
    /// </remarks>
    public static RegisterControlRecord? TryExtract(IReadOnlyList<TransactionModel>? transactions)
    {
        if (transactions is null)
        {
            return null;
        }

        foreach (var tx in transactions)
        {
            if (tx.MetaData?.TransactionType != TransactionType.Control)
            {
                continue;
            }

            if (tx.Payloads is null || tx.Payloads.Length == 0 || string.IsNullOrEmpty(tx.Payloads[0].Data))
            {
                continue;
            }

            try
            {
                var json = Encoding.UTF8.GetString(ContentEncodings.DecodeBase64Auto(tx.Payloads[0].Data));
                var record = JsonSerializer.Deserialize<RegisterControlRecord>(json);
                if (record is not null && !string.IsNullOrWhiteSpace(record.Name))
                {
                    return record;
                }
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                // Not a decodable control record — try the next transaction.
            }
        }

        return null;
    }
}
