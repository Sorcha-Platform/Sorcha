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
    /// Gets or sets the trust attestation for this decision (Entity 2 / FR-009).
    /// </summary>
    [JsonPropertyName("attestation")]
    public Attestation? Attestation { get; set; }

    /// <summary>
    /// Computes the canonical bytes the attestation signs over — the decision with its
    /// <see cref="Attestation"/> excluded (the signature cannot sign over itself).
    /// Uses <see cref="RegisterSerializationOptions.Canonical"/> so producer and validator agree.
    /// </summary>
    /// <returns>UTF-8 canonical JSON of the attestation-free decision.</returns>
    public byte[] ComputeSignableBytes()
    {
        var signable = new RoutingDecision
        {
            CompletedActionId = CompletedActionId,
            NextActions = NextActions,
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
