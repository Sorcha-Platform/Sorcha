// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Models.Verification;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Citizen-as-verifier engine (Feature 125, T037). Consumes a presenter's
/// SD-JWT VC + delegation credential, runs the OID4VP-aligned verification
/// pipeline (issuer signature → holder→device delegation → status list →
/// nonce/audience binding), and returns a <see cref="VerificationResult"/>
/// for the trust panel.
/// </summary>
/// <remarks>
/// <para>
/// v1 ships the interface plus a stub implementation in the wallet PWA;
/// the real local-validation path lifts <c>VerifiablePresentationValidator</c>
/// out of <c>Sorcha.Verifier</c> into a shared library so the desk verifier
/// and the wallet share one engine. That extraction is a follow-up refactor;
/// in the meantime, doorstep verification runs through the stub which
/// surfaces a clear "not yet wired" panel and is useful for UI testing.
/// </para>
/// <para>
/// Verifier identity is supplied by
/// <c>IEphemeralVerifierIdentityService</c> from PR-A — the verifier
/// generates a fresh EC P-256 key per session, used as the OID4VP
/// <c>client_id</c> for audience binding. The engine never persists the
/// presenter's credential bytes; the historical record uses display
/// metadata only.
/// </para>
/// </remarks>
public interface IVerifierEngine
{
    /// <summary>
    /// Verify a presenter's offer. The offer is the full OID4VP-style
    /// envelope as scanned from a QR code, tapped from an NFC tag, or
    /// pasted into the wallet's manual-entry box. The verifier's
    /// ephemeral identity is bound to the audience check; the
    /// caller is responsible for minting the identity for the session.
    /// </summary>
    Task<VerificationResult> VerifyAsync(VerifierEngineRequest request, CancellationToken ct = default);
}

/// <summary>Input to <see cref="IVerifierEngine.VerifyAsync"/>.</summary>
/// <param name="OfferPayload">
/// The scanned / NFC-tapped / pasted offer payload — typically an OID4VP
/// request URI containing a vp_token, a delegation credential, and any
/// supporting metadata. Engine implementations are free to parse this
/// according to the OID4VP profile they support.
/// </param>
/// <param name="VerifierClientId">
/// The verifier's ephemeral <c>client_id</c> for this session (RFC 7638
/// thumbprint of the per-session EC P-256 public JWK). Used as the audience
/// for the KB-JWT and nonce checks.
/// </param>
/// <param name="Nonce">Per-session random nonce — wallet must echo it.</param>
public sealed record VerifierEngineRequest(
    string OfferPayload,
    string VerifierClientId,
    string Nonce);
