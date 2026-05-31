// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// The pluggable trust mechanism for a <see cref="RoutingDecision"/> (Feature 145, Entity 2).
/// v1 ships only <see cref="AttestationKind.SenderSigned"/>; the other variants are the
/// reserved upgrade seam. The instance projection reads
/// <see cref="RoutingDecision.NextActions"/> regardless of variant — only validation branches
/// on <see cref="Kind"/>.
/// </summary>
public class Attestation
{
    /// <summary>
    /// Gets or sets the attestation variant. Defaults to <see cref="AttestationKind.SenderSigned"/>.
    /// </summary>
    [JsonPropertyName("kind")]
    public AttestationKind Kind { get; set; } = AttestationKind.SenderSigned;

    /// <summary>
    /// Gets or sets the sender wallet signature over the canonical, attestation-free decision
    /// (<see cref="RoutingDecision.ComputeSignableBytes"/>). Populated for
    /// <see cref="AttestationKind.SenderSigned"/> (v1).
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}

/// <summary>
/// Discriminates the attestation strength a routing decision carries. The required strength
/// is a per-register governance policy (default <see cref="SenderSigned"/>); reserved values
/// are rejected at seal until their implementation lands.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AttestationKind>))]
public enum AttestationKind
{
    /// <summary>v1 — signed by the authorised sender wallet. Validator checks the signature
    /// plus structural successor; it does NOT decrypt payload or re-evaluate the condition.</summary>
    SenderSigned,

    /// <summary>v2 (reserved) — a control-plane disclosure lets the validator re-evaluate the
    /// route condition over disclosed control fields.</summary>
    ValidatorReEvaluated,

    /// <summary>v3 (reserved) — a succinct, universally-verifiable proof that the next actions
    /// follow from the route graph and committed inputs, with no disclosure.</summary>
    Proof,
}
