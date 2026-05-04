// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// Categorises an SD-JWT verification failure so consumers can branch on a stable
/// enum value rather than substring-matching the human-readable message.
///
/// Background (issue #221 item 2): callers like
/// <c>PresentationRequestService.VerifyVpTokenSignatureAsync</c> previously had to
/// substring-match against <c>SdJwtService</c>'s internal error text to map a
/// failure to a coarse outcome. That coupling broke silently when the error
/// wording drifted. This enum and the accompanying <see cref="SdJwtError"/> type
/// make the mapping structural.
/// </summary>
public enum SdJwtErrorKind
{
    /// <summary>Default / not categorised. Treat as a hard signature failure for safety.</summary>
    Unknown = 0,

    /// <summary>The SD-JWT structure is malformed (missing JWT segment, wrong segment count, etc.).</summary>
    InvalidFormat,

    /// <summary>The JWS signature on the issuer-signed JWT failed cryptographic verification.</summary>
    SignatureInvalid,

    /// <summary>A disclosure could not be parsed, or its hash did not match the digest in the SD-JWT payload.</summary>
    DisclosureIntegrityFailure,

    /// <summary>The Key Binding JWT was missing, malformed, or its signature did not match the holder's confirmation key.</summary>
    KeyBindingFailure,

    /// <summary>The Key Binding JWT's <c>iat</c> claim was missing or outside the accepted clock-skew window.</summary>
    ClockSkew,

    /// <summary>The Key Binding JWT's <c>aud</c> claim did not match the expected audience.</summary>
    AudienceMismatch,

    /// <summary>The Key Binding JWT's <c>nonce</c> claim did not match the verifier-supplied nonce.</summary>
    NonceMismatch,

    /// <summary>The Key Binding JWT's <c>sd_hash</c> claim did not match the actual presentation content.</summary>
    SdHashMismatch,

    /// <summary>The verifier threw an unexpected exception while processing the token.</summary>
    VerificationException
}

/// <summary>
/// A single SD-JWT verification error: the structural <see cref="Kind"/>
/// (for branching) and the human-readable <see cref="Message"/>
/// (for diagnostics and logs).
/// </summary>
public class SdJwtError
{
    /// <summary>Structural categorisation of the failure.</summary>
    public required SdJwtErrorKind Kind { get; init; }

    /// <summary>Human-readable message suitable for logs. Do not surface to API consumers verbatim — may leak internals.</summary>
    public required string Message { get; init; }
}
