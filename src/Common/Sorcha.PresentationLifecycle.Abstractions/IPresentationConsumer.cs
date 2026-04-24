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
///   <item><description>VerifiedClaims MUST be filtered to the claims the blueprint's requiredClaims asked for — minimal disclosure.</description></item>
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
}
