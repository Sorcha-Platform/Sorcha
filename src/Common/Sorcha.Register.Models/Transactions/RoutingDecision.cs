// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// A routing decision carried on an action transaction's clear metadata (Feature 145).
/// Records the action this transaction completes and the <b>full</b> set of next actions
/// (preserving parallel branches), trusted via a pluggable <see cref="Attestation"/>.
/// Replaces the legacy singular next-action hint (Feature 145 — fully removed).
/// </summary>
/// <remarks>
/// The decision rides on the transaction in the clear so every node can advance instance
/// control state without decrypting the payload (FR-010). It is signed over its canonical
/// bytes (excluding the attestation) — see <see cref="ComputeSignableBytes"/>.
/// </remarks>
public class RoutingDecision
{
    /// <summary>
    /// Gets or sets the identifier of the action this transaction completes.
    /// </summary>
    [JsonPropertyName("completedActionId")]
    public int CompletedActionId { get; set; }

    /// <summary>
    /// Gets or sets the full set of next actions. Empty means this branch terminates.
    /// Multiple entries express parallel/fan-out branches (preserved end-to-end, FR-007).
    /// </summary>
    [JsonPropertyName("nextActions")]
    public List<ActionRef> NextActions { get; set; } = [];

    /// <summary>
    /// Gets or sets the identifier of the route the sender actually took (Feature 184).
    /// </summary>
    /// <remarks>
    /// Lets any node that folds this transaction find the taken route in the replicated blueprint
    /// without re-evaluating routing conditions — which it could not do anyway, having no access to
    /// the (possibly encrypted) payload. Null on transactions sealed before Feature 184 and on
    /// presentation-outcome decisions.
    /// </remarks>
    [JsonPropertyName("routeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RouteId { get; set; }

    /// <summary>
    /// Gets or sets a non-sensitive code describing <i>why</i> the sender decided as it did
    /// (Feature 184). Set only when the taken route declares an <c>x-decision-notice</c> whose
    /// <c>reasonCodeField</c> resolves against the submitted payload.
    /// </summary>
    /// <remarks>
    /// The code rides the transaction in the clear and is readable by every node holding the
    /// register, so it MUST describe the <i>class</i> of reason and never carry applicant data or
    /// free prose. The recipient's node resolves it to citizen-facing text from the blueprint's
    /// <c>reasons</c> catalogue.
    /// </remarks>
    [JsonPropertyName("reasonCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; set; }

    /// <summary>
    /// Gets or sets the id of the transaction that PUBLISHED the blueprint definition this action was
    /// executed against — the instance's <i>pin</i> (Feature 194, retyped by Feature 195).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under Feature 145 an instance is a deterministic projection of the sealed ledger, so which
    /// definition it runs cannot be a per-node lookup: a value two nodes cannot both derive from
    /// sealed transactions is a value they can diverge on. Carrying it here makes it a sealed fact.
    /// </para>
    /// <para>
    /// <b>Feature 195 changed what this value IS.</b> It was the executable-definition hash — a
    /// content address over a hand-written projection of the blueprint, which omitted several fields
    /// that genuinely affect execution, so definitions that differed behaviourally could share a pin.
    /// It is now the publication transaction id, which addresses the WHOLE definition and names a
    /// ledger fact: any node holding the register can resolve it, and the starting action chains from
    /// that very transaction. Anchor and pin became one value because they are one fact.
    /// </para>
    /// <para>
    /// Set to the definition the instance was created against on a starting action — that is the
    /// moment the instance's definition is chosen — and to the <b>instance's established pin</b> on
    /// every action thereafter. A subsequent action claiming a different definition is refused: a
    /// sender must not be able to move a running instance onto another definition by asserting one.
    /// </para>
    /// <para>
    /// Nullable, and omitted from the wire when null, so a transaction sealed before Feature 194
    /// deserialises cleanly and its original signature still verifies. New code never writes null.
    /// </para>
    /// </remarks>
    [JsonPropertyName("blueprintDefinitionTxId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlueprintDefinitionTxId { get; set; }

    /// <summary>
    /// Gets or sets the trust attestation for this decision (Entity 2 / FR-009).
    /// </summary>
    [JsonPropertyName("attestation")]
    public Attestation? Attestation { get; set; }

    /// <summary>
    /// Computes the canonical bytes the attestation signs over — the decision with its
    /// <see cref="Attestation"/> excluded (the signature cannot sign over itself).
    /// Uses <see cref="RegisterSerializationOptions.Canonical"/> so producer and validator agree.
    /// </summary>
    /// <remarks>
    /// This rebuilds the decision field by field. <b>Every new field MUST be copied here</b> — one
    /// omitted from this object rides the wire unauthenticated while appearing signed.
    /// </remarks>
    /// <returns>UTF-8 canonical JSON of the attestation-free decision.</returns>
    public byte[] ComputeSignableBytes()
    {
        var signable = new RoutingDecision
        {
            CompletedActionId = CompletedActionId,
            NextActions = NextActions,
            RouteId = RouteId,
            ReasonCode = ReasonCode,
            BlueprintDefinitionTxId = BlueprintDefinitionTxId,
            Attestation = null,
        };
        return JsonSerializer.SerializeToUtf8Bytes(signable, RegisterSerializationOptions.Canonical);
    }
}

/// <summary>
/// A reference to a next action within a routing decision. <see cref="BranchKey"/>
/// distinguishes parallel branches where the route graph needs it.
/// </summary>
public class ActionRef
{
    /// <summary>
    /// Gets or sets the next action identifier.
    /// </summary>
    [JsonPropertyName("actionId")]
    public int ActionId { get; set; }

    /// <summary>
    /// Gets or sets an optional branch discriminator for parallel branches.
    /// </summary>
    [JsonPropertyName("branchKey")]
    public string? BranchKey { get; set; }
}
