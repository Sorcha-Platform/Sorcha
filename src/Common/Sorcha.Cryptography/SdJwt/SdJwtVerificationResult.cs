// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// Result of verifying an SD-JWT token or presentation.
/// </summary>
public class SdJwtVerificationResult
{
    /// <summary>
    /// Whether the token/presentation is cryptographically valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Disclosed claims extracted from the verified token.
    /// </summary>
    public Dictionary<string, object> Claims { get; set; } = new();

    /// <summary>
    /// Verification error messages, if any. Plain-string projection of <see cref="ErrorDetails"/>;
    /// retained for backward compatibility with existing log call sites and assertion tests.
    /// New consumers should branch on <see cref="ErrorDetails"/> + <see cref="SdJwtError.Kind"/>
    /// instead of substring-matching these strings (issue #221).
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Structured verification errors, each carrying a typed <see cref="SdJwtErrorKind"/>
    /// and the same human-readable message that appears in <see cref="Errors"/>. Populated
    /// in lock-step with <see cref="Errors"/> by <c>SdJwtService</c>.
    /// </summary>
    public List<SdJwtError> ErrorDetails { get; set; } = new();

    /// <summary>
    /// The issuer identifier from the token payload.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// The subject identifier from the token payload.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Issuance timestamp from the token payload.
    /// </summary>
    public DateTimeOffset? IssuedAt { get; set; }

    /// <summary>
    /// Expiration timestamp from the token payload.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// The holder's confirmation key (cnf.jwk) extracted from the token payload,
    /// if present. Used by the verifier to validate Key Binding JWTs.
    /// </summary>
    public JsonElement? CnfJwk { get; set; }

    /// <summary>
    /// Whether the Key Binding JWT was present and successfully verified
    /// against the holder's confirmation key.
    /// </summary>
    public bool HolderKeyVerified { get; set; }
}
