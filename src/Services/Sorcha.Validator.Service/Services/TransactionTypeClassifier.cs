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
    /// life of the network.
    /// </summary>
    /// <remarks>
    /// Genesis is <b>not exempt</b> from the freshness check — it is subject to a
    /// <b>separate, short window</b> (<see cref="Configuration.ValidationEngineConfiguration.GenesisMaxAge"/>,
    /// default 1h) instead of the live-transaction window
    /// (<see cref="Configuration.ValidationEngineConfiguration.MaxTransactionAge"/>); see
    /// <c>ValidateTiming</c> / <c>VAL_TIME_002</c>. SECURITY: a stale-but-accepted genesis is a
    /// replay vector, so the bound forces a regenerated system register to be minted, deployed, and
    /// bootstrapped within the window. This gates the <b>ingest-and-seal</b> path (Auto bootstrap);
    /// a node that <b>pulls an already-sealed genesis docket</b> verifies the docket's validator
    /// signature + chain (not the genesis tx's age), so late-joining SyncOnly replicas are
    /// unaffected by the window.
    /// </remarks>
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
        var signedType = SignedPayloadType(transaction);

        return IsLifecyclePayloadType(signedType, includeInitiated: true);
    }

    /// <summary>
    /// Signed lifecycle payload types, as written INSIDE the payload by
    /// <c>TransactionBuilderServiceExtensions.BuildPresentation*Async</c> before signing.
    /// </summary>
    private const string PayloadTypeInitiated = "presentation-initiated";
    private const string PayloadTypeOutcome = "presentation-outcome";
    private const string PayloadTypeAbandoned = "presentation-abandoned";

    private static bool IsLifecyclePayloadType(string? payloadType, bool includeInitiated)
    {
        if (payloadType is null) return false;

        if (includeInitiated
            && string.Equals(payloadType, PayloadTypeInitiated, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(payloadType, PayloadTypeOutcome, StringComparison.OrdinalIgnoreCase)
            || string.Equals(payloadType, PayloadTypeAbandoned, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the transaction type from the <b>signed</b> payload.
    ///
    /// <para>C-VAL (catch-up security review 2026-07-29): the lifecycle predicates used to read
    /// <c>Metadata["Type"]</c>. Metadata is NOT part of the signed data (which is
    /// <c>"{TransactionId}:{PayloadHash}"</c>), NOT part of <c>PayloadHash</c> (only
    /// <c>Payload</c> is hashed), and NOT part of the docket merkle leaf — so it is freely
    /// settable by anyone who can submit, with nothing detecting the change. Because these
    /// predicates gate the action-schema check, the routing-decision attestation and the
    /// VAL_BP_003 reachability check, one unsigned string could disable all three at once on an
    /// arbitrary transaction. <c>Payload.type</c> is inside the hash the signature covers, so it
    /// is the only trustworthy discriminator. <c>IsRejectionTransaction</c> already consulted the
    /// payload for the same reason.</para>
    /// </summary>
    private static string? SignedPayloadType(Transaction transaction)
    {
        if (transaction.Payload.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;

        if (!transaction.Payload.TryGetProperty("type", out var payloadType))
            return null;

        return payloadType.ValueKind == System.Text.Json.JsonValueKind.String
            ? payloadType.GetString()
            : null;
    }

    /// <summary>
    /// True when <c>Metadata["Type"]</c> claims a presentation-lifecycle transaction but the
    /// signed payload does not corroborate it. Never used to grant an exemption — only so the
    /// engine can record that something asked for a lifecycle carve-out it was not entitled to,
    /// which is what an attempted schema-validation bypass looks like on the wire.
    /// </summary>
    public static bool HasUncorroboratedLifecycleMetadata(Transaction transaction)
    {
        if (!transaction.Metadata.TryGetValue("Type", out var typeStr) || typeStr is null)
            return false;

        var claimsLifecycle =
            string.Equals(typeStr, "PresentationInitiated", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeStr, "PresentationOutcome", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeStr, "PresentationAbandoned", StringComparison.OrdinalIgnoreCase);

        return claimsLifecycle && !IsLifecycleTransaction(transaction);
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
        // Same signed-payload rule as IsLifecycleTransaction — this predicate waives the
        // VAL_BP_003 reachability check and the routing-decision attestation, so it must not be
        // reachable from unsigned metadata either.
        return IsLifecyclePayloadType(SignedPayloadType(transaction), includeInitiated: false);
    }
}
