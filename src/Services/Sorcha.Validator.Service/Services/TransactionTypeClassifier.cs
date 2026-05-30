// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Constants;
using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Centralised transaction-type predicates used by validator rule logic.
/// Carved out of <see cref="ValidationEngine"/> as part of the post-Feature-119
/// rule-base cleanup so that exemptions and carve-outs reference a single
/// predicate registry rather than reimplementing string comparisons inline.
/// All predicates are pure and depend only on the transaction's structural
/// fields (BlueprintId, Metadata, Payload).
/// </summary>
internal static class TransactionTypeClassifier
{
    /// <summary>
    /// True for transactions that bypass action-schema validation and per-sender replay
    /// protection: genesis, governance Control, and (post-#876) BlueprintPublish. All three
    /// are administrative — signed by the system wallet (or in genesis's case the offline
    /// ceremony key) and have no per-sender sequence; they carry a free-form ActionId
    /// (<c>"blueprint-publish"</c>, etc.) that can't and shouldn't parse as an int.
    /// </summary>
    public static bool IsGenesisOrControlTransaction(Transaction transaction)
    {
        if (string.Equals(transaction.BlueprintId, GenesisConstants.BlueprintId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (transaction.Metadata.TryGetValue("Type", out var typeStr) &&
            (string.Equals(typeStr, "Genesis", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(typeStr, "Control", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(typeStr, "BlueprintPublish", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>
    /// True only for the pre-signed genesis transaction (the network trust anchor),
    /// NOT live control/governance transactions. The genesis transaction is signed
    /// once during the offline ceremony with a fixed timestamp and embedded for the
    /// life of the network — it is ingested whenever a node bootstraps, which may be
    /// arbitrarily long after the ceremony. It must therefore be exempt from the
    /// transaction-freshness window (VAL_TIME_002); live control transactions are
    /// created at submission time and stay subject to it.
    /// </summary>
    public static bool IsGenesisTransaction(Transaction transaction)
    {
        if (string.Equals(transaction.BlueprintId, GenesisConstants.BlueprintId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (transaction.Metadata.TryGetValue("Type", out var typeStr) &&
            string.Equals(typeStr, "Genesis", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool IsParticipantTransaction(Transaction transaction)
    {
        return transaction.Metadata.TryGetValue("Type", out var typeStr) &&
               string.Equals(typeStr, "Participant", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRejectionTransaction(Transaction transaction)
    {
        if (transaction.Metadata.TryGetValue("Type", out var typeStr) &&
            string.Equals(typeStr, "Rejection", StringComparison.OrdinalIgnoreCase))
            return true;

        if (transaction.Payload.ValueKind == System.Text.Json.JsonValueKind.Object &&
            transaction.Payload.TryGetProperty("type", out var payloadType) &&
            string.Equals(payloadType.GetString(), "rejection", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool IsRevocationTransaction(Transaction transaction)
    {
        if (transaction.Metadata.TryGetValue("Type", out var typeStr) &&
            string.Equals(typeStr, "Revocation", StringComparison.OrdinalIgnoreCase))
            return true;

        if (transaction.Metadata.TryGetValue("transactionType", out var txType) &&
            string.Equals(txType, "Revocation", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// True if the transaction is a presentation lifecycle event:
    /// <c>PresentationInitiated</c>, <c>PresentationOutcome</c>, or
    /// <c>PresentationAbandoned</c>. The general predicate — useful for routing
    /// lifecycle events to the lifecycle-specific code paths in Blueprint and
    /// Validator services.
    /// </summary>
    public static bool IsLifecycleTransaction(Transaction transaction)
    {
        if (!transaction.Metadata.TryGetValue("Type", out var typeStr) || typeStr is null)
            return false;

        return string.Equals(typeStr, "PresentationInitiated", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeStr, "PresentationOutcome", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeStr, "PresentationAbandoned", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if the transaction is an intra-action lifecycle terminal —
    /// <c>PresentationOutcome</c> or <c>PresentationAbandoned</c>. These are
    /// the lifecycle events that chain off the same action's
    /// <c>PresentationInitiated</c> and therefore carry the same ActionId,
    /// which would trip VAL_BP_003 reflexively. Excludes
    /// <c>PresentationInitiated</c> — it really does advance from action N-1
    /// to action N and gets the full reachability check.
    /// </summary>
    public static bool IsIntraActionLifecycleTerminal(Transaction transaction)
    {
        if (!transaction.Metadata.TryGetValue("Type", out var typeStr) || typeStr is null)
            return false;

        return string.Equals(typeStr, "PresentationOutcome", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(typeStr, "PresentationAbandoned", StringComparison.OrdinalIgnoreCase);
    }
}
