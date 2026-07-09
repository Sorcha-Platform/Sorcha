// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Outcome of evaluating a credential against a <see cref="TrustPolicy"/> (feature 135).
/// The single result type both verification paths consume.
/// </summary>
public class TrustDecision
{
    /// <summary>Whether the credential is trusted under the policy.</summary>
    public bool IsTrusted { get; set; }

    /// <summary>
    /// Whether the issuer's cryptographic signature verified. Always set truthfully — the
    /// historical "unverified, defer to service layer" shortcut is removed (FR-008).
    /// </summary>
    public bool SignatureValid { get; set; }

    /// <summary>The assurance level actually established.</summary>
    public AssuranceLevel EstablishedAssurance { get; set; } = AssuranceLevel.Low;

    /// <summary>
    /// True when the credential is accepted at reduced assurance (a Warn): the signature verified but
    /// no trust source vouched, and the policy opted into <c>WarnOnUnlistedVerifiedIssuer</c>. Consumers
    /// that treat <see cref="IsTrusted"/> as "accept" MUST honour this to distinguish a Warn from a
    /// full-trust Pass (feature 177).
    /// </summary>
    public bool ReducedAssurance { get; set; }

    /// <summary>Which source(s) vouched for the issuer.</summary>
    public List<TrustSourceKind> DecidingSources { get; set; } = [];

    /// <summary>The failure reason when <see cref="IsTrusted"/> is false.</summary>
    public TrustFailureReason? FailureReason { get; set; }

    /// <summary>Human-readable detail for diagnostics (no credential subject data).</summary>
    public string? Message { get; set; }

    /// <summary>The audit record of the decision (populated on accept; best-effort on reject).</summary>
    public TrustEvidence Evidence { get; set; } = new();

    /// <summary>Convenience factory for a fail-closed rejection.</summary>
    public static TrustDecision Reject(TrustFailureReason reason, string? message = null) =>
        new() { IsTrusted = false, FailureReason = reason, Message = message };
}
