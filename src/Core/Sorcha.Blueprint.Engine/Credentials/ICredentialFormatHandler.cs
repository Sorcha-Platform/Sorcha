// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// A presented credential awaiting verification, format-agnostic (feature 135).
/// </summary>
public class PresentedCredential
{
    /// <summary>The raw presentation (SD-JWT compact form, or base64url mdoc DeviceResponse).</summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>The format of <see cref="Raw"/>.</summary>
    public CredentialFormat Format { get; set; }

    /// <summary>Expected audience for holder-binding (verifier identifier / client_id).</summary>
    public string? ExpectedAudience { get; set; }

    /// <summary>Expected nonce for holder-binding / freshness.</summary>
    public string? ExpectedNonce { get; set; }

    /// <summary>
    /// mso_mdoc only — the OpenID4VP <c>response_uri</c> needed to reconstruct the mdoc
    /// <c>SessionTranscript</c> (with <see cref="ExpectedAudience"/> as client_id and
    /// <see cref="ExpectedNonce"/> as the nonce). Ignored for SD-JWT VC.
    /// </summary>
    public string? ExpectedResponseUri { get; set; }

    /// <summary>
    /// mso_mdoc only — the optional JWK SHA-256 thumbprint that binds the response to the verifier's
    /// ephemeral key in the OpenID4VP handover. Null when not used.
    /// </summary>
    public byte[]? ExpectedJwkThumbprint { get; set; }
}

/// <summary>
/// Result of verifying a presented credential through a format handler (feature 135).
/// </summary>
public class FormatVerifyResult
{
    /// <summary>Whether verification (signature, integrity, holder-binding, trust, status) succeeded.</summary>
    public bool IsValid { get; set; }

    /// <summary>The trust decision produced by the unified evaluator.</summary>
    public TrustDecision? Trust { get; set; }

    /// <summary>The disclosed claims, surfaced uniformly across formats.</summary>
    public Dictionary<string, object> DisclosedClaims { get; set; } = new();

    /// <summary>The resolved issuer identifier.</summary>
    public string IssuerId { get; set; } = string.Empty;

    /// <summary>Failure detail for diagnostics.</summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Per-format issue / present / verify abstraction (feature 135). Implementations own the
/// format-specific cryptography and wire encoding; they delegate the trust decision to
/// <see cref="ITrustEvaluator"/> so trust semantics are identical across formats.
/// </summary>
public interface ICredentialFormatHandler
{
    /// <summary>The credential format this handler implements.</summary>
    CredentialFormat Format { get; }

    /// <summary>
    /// Verifies a presented credential against a requirement: signature + integrity +
    /// holder-binding (format-specific), then trust + status via the shared evaluator.
    /// </summary>
    Task<FormatVerifyResult> VerifyAsync(
        PresentedCredential presentation,
        CredentialRequirement requirement,
        CancellationToken cancellationToken = default);
}
