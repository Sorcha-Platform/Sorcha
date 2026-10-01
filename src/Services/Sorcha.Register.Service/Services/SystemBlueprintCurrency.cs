// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Decides which of several publications of one system blueprint is the current one, from the
/// ledger's own order: docket number, then position within the docket's <c>TransactionIds</c>.
/// </summary>
/// <remarks>
/// <see cref="TransactionModel.TimeStamp"/> is deliberately never read: it is assigned by the
/// submitting node and is not part of the sealed ordering, so a later-sealed publication can carry
/// an earlier timestamp. A publication that cannot be placed in a sealed docket (no docket number,
/// unknown docket, or absent from its docket's transaction list) has no ledger position and is
/// excluded and logged rather than guessed at.
/// </remarks>
public static class SystemBlueprintCurrency
{
    /// <summary>
    /// Orders publications oldest to newest by ledger position, excluding any that cannot be placed.
    /// </summary>
    /// <param name="publications">Publications of one blueprint.</param>
    /// <param name="docketLookup">Resolves a docket number to its header, or null if unknown.</param>
    /// <param name="logger">Receives a structured warning for each excluded publication.</param>
    public static IReadOnlyList<TransactionModel> OrderByLedger(
        IEnumerable<TransactionModel> publications,
        Func<ulong, DocketHeader?> docketLookup,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(publications);
        ArgumentNullException.ThrowIfNull(docketLookup);
        ArgumentNullException.ThrowIfNull(logger);

        var placed = new List<(TransactionModel Tx, ulong Docket, int Index)>();

        foreach (var tx in publications)
        {
            if (tx.DocketNumber is not { } docketNumber)
            {
                logger.LogWarning(
                    "System blueprint publication {TxId} has no docket number; excluded from currency ordering",
                    tx.TxId);
                continue;
            }

            var docket = docketLookup(docketNumber);
            if (docket is null)
            {
                logger.LogWarning(
                    "System blueprint publication {TxId} names docket {DocketNumber} which could not be resolved; excluded from currency ordering",
                    tx.TxId, docketNumber);
                continue;
            }

            var index = docket.TransactionIds.IndexOf(tx.TxId);
            if (index < 0)
            {
                logger.LogWarning(
                    "System blueprint publication {TxId} is not listed in docket {DocketNumber}; excluded from currency ordering",
                    tx.TxId, docketNumber);
                continue;
            }

            placed.Add((tx, docketNumber, index));
        }

        return placed
            .OrderBy(p => p.Docket)
            .ThenBy(p => p.Index)
            .Select(p => p.Tx)
            .ToList();
    }

    /// <summary>The newest publication by ledger position, or null if none can be placed.</summary>
    public static TransactionModel? Current(
        IEnumerable<TransactionModel> publications,
        Func<ulong, DocketHeader?> docketLookup,
        ILogger logger)
        => OrderByLedger(publications, docketLookup, logger).LastOrDefault();

    /// <summary>
    /// The 1-based ordinal of a publication in ledger order, or null if it is not among the
    /// placeable publications.
    /// </summary>
    public static int? VersionOf(
        string txId,
        IEnumerable<TransactionModel> publications,
        Func<ulong, DocketHeader?> docketLookup,
        ILogger logger)
    {
        var ordered = OrderByLedger(publications, docketLookup, logger);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].TxId, txId, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return null;
    }
}
