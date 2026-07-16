// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Request body of <c>POST /api/v1/wallet/presentations/sign-kb</c> (#1195 Phase 2, Task 6a —
/// server-custody KB-JWT signing for holder-<c>cnf</c> presentations). Carries ONLY the JWS
/// signing input; the holder key that signs is always resolved from the authenticated caller's
/// JWT — there is deliberately no wallet-address field, so a citizen can only ever sign under
/// their OWN holder key.
/// </summary>
public sealed record KbJwtSignRequest
{
    /// <summary>
    /// The compact JWS signing input — <c>base64url(header).base64url(payload)</c> of the
    /// KB-JWT to sign. The decoded header MUST carry <c>typ: "kb+jwt"</c>; the endpoint refuses
    /// anything else (the holder key also signs device delegation credentials, so it must not
    /// become a general-purpose signing oracle).
    /// </summary>
    public required string SigningInput { get; init; }
}

/// <summary>
/// Response body of <c>POST /api/v1/wallet/presentations/sign-kb</c>.
/// </summary>
public sealed record KbJwtSignResponse
{
    /// <summary>The raw signature over the ASCII bytes of the signing input, base64url-encoded — ready to append as the KB-JWT's third segment.</summary>
    public required string Signature { get; init; }

    /// <summary>The JOSE algorithm the holder key signed with (<c>ES256</c> or <c>EdDSA</c>). Matches the header's <c>alg</c> (a mismatch is refused with 400).</summary>
    public required string Algorithm { get; init; }
}
