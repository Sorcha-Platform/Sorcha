// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Contract implemented by any verifier that participates in the Timebound
/// Presentation Lifecycle. The consumer verifies a callback payload (format
/// is consumer-specific) and returns a <see cref="PresentationOutcome"/>; the
/// lifecycle service in Blueprint Service handles transaction writing, sentinel
/// management, and workflow routing.
/// </summary>
/// <remarks>
/// Contract invariants (see <c>specs/111-presentation-lifecycle/contracts/consumer-contract.md</c>
/// for full details):
/// <list type="bullet">
///   <item><description><see cref="VerifyAsync"/> is synchronous w.r.t. the caller's await — no background processing after return.</description></item>
///   <item><description>Consumers MUST NOT write to the register directly; they return an outcome, the lifecycle service writes it.</description></item>
///   <item><description>Consumer-level idempotency is NOT required; the lifecycle service guards via a Redis sentinel.</description></item>
///   <item><description>VerifiedClaims MUST contain only claims from the VERIFIED presentation
///   (issuer-committed, digest-anchored) and MUST cover every requiredClaim (the gate). Claims
///   beyond the required set MAY be included when the citizen consented to disclose them —
///   minimal disclosure is enforced at the wallet's consent, not by server-side truncation
///   (#1195 Phase 2: the full-disclosure bind gate copies exactly what the root carries).</description></item>
///   <item><description>VerifierDiagnostics format is consumer-defined; SHOULD NOT contain PII.</description></item>
/// </list>
/// </remarks>
public interface IPresentationConsumer
{
    /// <summary>
    /// Stable short identifier for this consumer (e.g. "haip",
    /// "file-upload-deadline"). Referenced by blueprints via
    /// credentialRequirements.PresentationSource and carried in the
    /// PresentationInitiated transaction metadata for audit.
    /// </summary>
    string ConsumerName { get; }

    /// <summary>
    /// Verify a verifier callback and return the lifecycle outcome.
    /// </summary>
    /// <param name="context">Pending-attempt context reconstructed by the
    /// lifecycle service from the transient store.</param>
    /// <param name="verifierPayload">Opaque consumer-specific payload; the
    /// lifecycle service does not interpret it. The consumer deserialises into
    /// its own types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success with verified claims, or decline with reason.</returns>
    Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Build the wallet-facing artifact (OID4VP request URI + optional alternative
    /// URIs) for a new presentation attempt. Called by the lifecycle service's
    /// <c>InitiateAsync</c> after the <c>presentation-initiated</c> transaction
    /// is written. The descriptor is returned to the calling page so it can
    /// render the hybrid universal QR / tap-link affordance.
    /// </summary>
    /// <remarks>
    /// <para>Default implementation throws <see cref="NotSupportedException"/>.
    /// Consumers using the HAIP-style hardcoded initiation path don't need to
    /// override — the lifecycle service falls back to its legacy path when this
    /// method throws. New consumers (Feature 127 introduces the first non-HAIP
    /// consumer, <c>"sorcha-wallet"</c>) override this method to opt into the
    /// generic initiation dispatch.</para>
    /// <para>Idempotency is not required at the consumer level. The lifecycle
    /// service guarantees one call per <c>presentationRequestId</c>.</para>
    /// <para>Side effects MUST be none — the method is pure. Writing register
    /// transactions, stashing pending state, and minting claims-fetch tokens
    /// are the lifecycle service's responsibility.</para>
    /// </remarks>
    Task<ConsumerInitiationDescriptor> BuildInitiationAsync(
        PresentationInitiationContext context,
        CancellationToken cancellationToken)
        => throw new NotSupportedException(
            $"Consumer '{ConsumerName}' does not implement BuildInitiationAsync. " +
            "If this is a new non-HAIP consumer, override the method; if HAIP, " +
            "the lifecycle service uses its existing hardcoded initiation path.");
}
