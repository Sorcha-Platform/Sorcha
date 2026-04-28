// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Resolves the Control transaction inside a cached genesis docket.
/// </summary>
/// <remarks>
/// <para>
/// The wire format separates dockets and transactions: <c>CachedDocket.Data</c>
/// is a JSON-serialised <c>Sorcha.Register.Models.Docket</c> which carries only
/// <c>TransactionIds</c>, while <c>CachedTransaction.Data</c> holds the full
/// canonical <c>TransactionModel</c> JSON for each transaction.
/// </para>
/// <para>
/// Earlier verifier and roster-extractor code parsed <c>CachedDocket.Data</c>
/// expecting an inline <c>transactions[]</c> array — that array does not exist
/// in the wire shape, so every replicated genesis docket got rejected
/// pre-finalisation. The fix is to iterate <c>docket.TransactionIds</c> and
/// look each transaction up in the cache, returning the first Control-typed
/// hit.
/// </para>
/// </remarks>
internal static class GenesisControlTransactionLookup
{
    /// <summary>
    /// Iterates the docket's transaction IDs, looks each up in the register
    /// cache, deserialises the canonical TransactionModel JSON, and returns
    /// the first one whose <c>MetaData.TransactionType</c> is
    /// <see cref="TransactionType.Control"/>.
    /// </summary>
    /// <returns>Null if no transactions are cached yet, no Control transaction is
    /// found, or the cached payload fails to deserialise.</returns>
    public static TransactionModel? TryFindControl(
        string registerId,
        CachedDocket docket,
        RegisterCache cache,
        ILogger? logger = null)
    {
        var entry = cache.Get(registerId);
        if (entry is null)
        {
            logger?.LogDebug(
                "GenesisControlTransactionLookup: no cache entry for register {RegisterId}",
                registerId);
            return null;
        }

        if (docket.TransactionIds is null || docket.TransactionIds.Count == 0)
        {
            logger?.LogDebug(
                "GenesisControlTransactionLookup: docket {DocketNumber} for register {RegisterId} has no transaction IDs",
                docket.Version, registerId);
            return null;
        }

        foreach (var txId in docket.TransactionIds)
        {
            var cached = entry.GetTransaction(txId);
            if (cached is null || cached.Data is null || cached.Data.Length == 0)
                continue;

            TransactionModel? tx;
            try
            {
                // Wire shape is canonical (camelCase) — case-sensitive deserialisation
                // is required so SenderWallet, MetaData, etc. populate correctly.
                tx = JsonSerializer.Deserialize<TransactionModel>(
                    cached.Data, RegisterSerializationOptions.Canonical);
            }
            catch (JsonException ex)
            {
                logger?.LogDebug(ex,
                    "GenesisControlTransactionLookup: failed to deserialise tx {TxId} for register {RegisterId}",
                    txId, registerId);
                continue;
            }

            if (tx is null) continue;
            if (tx.MetaData?.TransactionType != TransactionType.Control) continue;

            // Backfill identity fields the canonical wire shape doesn't always carry —
            // matches DocketFinalizationService.BuildDocketModel for consistency.
            tx.RegisterId = registerId;
            tx.TxId = txId;
            return tx;
        }

        return null;
    }
}
