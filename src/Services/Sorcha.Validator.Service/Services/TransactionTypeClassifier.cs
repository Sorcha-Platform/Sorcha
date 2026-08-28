// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;
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
    /// <b>REMOVED in Feature 196 (#1591).</b> This is where the six administrative exemptions used to
    /// be granted, from either <c>BlueprintId == "genesis"</c> or
    /// <c>Metadata["Type"] in {Genesis, Control, BlueprintPublish}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither field is signed. The signed bytes are <c>"{TransactionId}:{PayloadHash}"</c>, and both
    /// of those are the same digest of the payload body — so a submitter chose both discriminators
    /// freely and nothing checked whether they were entitled to what they claimed. Since one of the
    /// six waivers is <c>VAL_BP_002</c> sender authorisation itself, a forged claim disabled the very
    /// check that would have caught the forger.
    /// </para>
    /// <para>
    /// Replaced by <see cref="IExemptionAuthorityResolver"/>, which grants an exemption only where the
    /// SIGNER is proved entitled to it. Deleted rather than deprecated: a predicate that grants
    /// privilege from an unsigned field is not safe to leave callable, and the compiler finding every
    /// call site is the point.
    /// </para>
    /// <para>
    /// Note the asymmetry this class already documented and which #1591 formalised: reading an
    /// unsigned field to <i>withdraw</i> an exemption is sound, because forging it can only subject
    /// the forger to more validation. Reading one to <i>grant</i> is not. That is why
    /// <see cref="IsGovernanceActionTransaction"/> below still reads unsigned fields and is correct.
    /// </para>
    /// </remarks>

    /// <summary>
    /// True for the three governance transactions submitted as numbered actions of
    /// <see cref="GovernanceBlueprint"/> — a proposal, an approval, and an enactment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this is for (Feature 189, T054).</b> Governance transactions carry
    /// <c>Metadata["Type"] = "Control"</c>, which buys them six exemptions at once: sequence-replay,
    /// action-schema validation, blueprint conformance, the routing-decision attestation, crypto
    /// policy, and — through the persisted <c>TransactionType</c> — fork detection. Only the schema
    /// exemption was ever deliberate for them, and it was a consequence of the governance blueprint
    /// having no published contract worth checking against rather than a decision that governance
    /// payloads should go unchecked. This predicate names the three so the schema exemption can be
    /// withdrawn from them specifically, leaving genesis and blueprint-publish untouched.
    /// </para>
    /// <para>
    /// <b>The other five exemptions are deliberately retained, and two of them are load-bearing.</b>
    /// Every approval sets <c>PreviousTransactionId</c> to the proposal, so N approvals are N
    /// children of one parent — a shape only the fork bypass permits. And every approval is the same
    /// action sent by the same participant role, which <c>VAL_BP_002</c>'s chain-derived binding
    /// treats as immutably bound to the first approver. Withdrawing either makes the second approval
    /// unreachable and quorum unattainable. Quorum is many signers on one step; an action chain is a
    /// line. Do not "finish the job" by widening this predicate's use to those checks without
    /// changing what the checks mean for every workflow on the platform.
    /// </para>
    /// <para>
    /// <b>Why an unsigned basis is sound HERE and nowhere else.</b> <c>BlueprintId</c>,
    /// <c>ActionId</c> and <c>Metadata</c> all sit outside the signature, so a submitter chooses
    /// them freely. That is tolerable only because this predicate is used to <i>withdraw</i> an
    /// exemption: forging it into returning <c>true</c> subjects the forger to more validation, and
    /// forging it into returning <c>false</c> leaves them exactly where they were before T054 —
    /// still facing the roster check, which <c>RightsEnforcementService</c> applies on the same
    /// <c>BlueprintId</c>. The reasoning inverts the moment this is used to grant anything, so if
    /// you reach for it to justify a carve-out, move the discriminator into the signed payload first
    /// (the C-VAL precedent that put the lifecycle predicates on <c>Payload.type</c>).
    /// </para>
    /// <para>
    /// The crypto-policy update is excluded by construction: it carries the free-form ActionId
    /// <c>"control.crypto.update"</c>, which is not one of the numbered actions and does not parse
    /// as an int. Its payload is a policy document, not a
    /// <c>ControlTransactionPayload</c>, and the blueprint declares no contract for it.
    /// </para>
    /// </remarks>
    public static bool IsGovernanceActionTransaction(Transaction transaction)
    {
        if (!string.Equals(
                transaction.BlueprintId, GovernanceBlueprint.BlueprintId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Publishing the governance blueprint is a system seed whose payload is the blueprint body,
        // not a governance action against it — the same distinction RightsEnforcementService draws
        // before applying the roster check (#917).
        if (transaction.Metadata.TryGetValue("transactionType", out var seedType)
            && string.Equals(seedType, "BlueprintPublish", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(transaction.ActionId, out var actionId)
            && actionId is GovernanceBlueprint.ProposeChangeActionId
                        or GovernanceBlueprint.CollectQuorumActionId
                        or GovernanceBlueprint.RecordControlTransactionActionId;
    }

    /// <summary>
    /// <b>REMOVED in Feature 196 (#1591).</b> Selected the short genesis freshness window from the
    /// same two unsigned fields, so claiming genesis was enough to be judged against
    /// <c>GenesisMaxAge</c> instead of the live-transaction window. Use
    /// <see cref="ExemptionDecision.IsGenesis"/> from <see cref="IExemptionAuthorityResolver"/>, which
    /// additionally requires the signing key to match this node's trust anchor.
    /// </summary>

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
