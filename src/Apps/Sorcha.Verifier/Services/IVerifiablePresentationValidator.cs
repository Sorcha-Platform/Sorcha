// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Verifier.Services.Models;

namespace Sorcha.Verifier.Services;

/// <summary>
/// Validates a wallet-submitted vp_token against an open <see cref="VerifierSession"/>.
/// Performs offline-friendly chain validation: issuer signature, holder→device delegation,
/// KB-JWT signature, status-list checks, claim disclosure consistency.
///
/// Per design §3 / data-model §C: the citizen presents an SD-JWT VC bound to their
/// holder key (cnf.jwk == holder JWK). The KB-JWT is signed by the device key, but
/// the device's authority comes from a separate device delegation credential signed
/// by the holder key. The validator therefore unwraps:
///
/// <list type="number">
///   <item>vp_token (SD-JWT VC) — issued + signed by an org, cnf.jwk = holder JWK</item>
///   <item>KB-JWT — signed by device key, audience and nonce match the session</item>
///   <item>Device delegation credential — separately included; signed by holder key, cnf.jwk = device JWK, status-list bit unset</item>
/// </list>
/// </summary>
public interface IVerifiablePresentationValidator
{
    /// <summary>Validate a wallet's <c>vp_token</c> + delegation credential against an open session.</summary>
    Task<VerificationOutcome> ValidateAsync(
        VerifierSession session,
        string vpToken,
        string? delegationCredential,
        CancellationToken ct = default);
}
