// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Trust escalation level derived from verification check results.
/// </summary>
public enum TrustEscalation
{
    /// <summary>All checks passed and no warnings present.</summary>
    Green,

    /// <summary>All checks passed but one or more warnings were raised.</summary>
    Amber,

    /// <summary>One or more checks failed.</summary>
    Red,
}

/// <summary>
/// A non-critical issue found during credential verification.
/// </summary>
/// <param name="Code">Machine-readable warning code.</param>
/// <param name="Message">Human-readable description.</param>
public record VerificationWarning(string Code, string Message);

/// <summary>
/// Result of verifying a verifiable credential presentation.
/// </summary>
public class VerificationResultViewModel
{
    // ── Five core verification checks ────────────────────────────────────────

    /// <summary>Whether the credential's cryptographic signature is valid.</summary>
    public required bool SignatureValid { get; set; }

    /// <summary>Whether the issuer DID is trusted by this organisation.</summary>
    public required bool IssuerTrusted { get; set; }

    /// <summary>Whether the credential has not been revoked.</summary>
    public required bool NotRevoked { get; set; }

    /// <summary>Whether the credential has not expired.</summary>
    public required bool NotExpired { get; set; }

    /// <summary>Whether all required claims are present in the presentation.</summary>
    public required bool RequiredClaimsPresent { get; set; }

    // ── Warnings ─────────────────────────────────────────────────────────────

    /// <summary>Non-critical issues found during verification.</summary>
    public required List<VerificationWarning> Warnings { get; set; }

    // ── Display properties ────────────────────────────────────────────────────

    /// <summary>Display name of the credential issuer.</summary>
    public required string IssuerName { get; set; }

    /// <summary>Human-readable credential type label.</summary>
    public required string CredentialType { get; set; }

    /// <summary>When the credential expires, if applicable.</summary>
    public required DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Claims that were disclosed in this presentation.</summary>
    public required Dictionary<string, object> DisclosedClaims { get; set; }

    // ── Computed properties ───────────────────────────────────────────────────

    /// <summary>
    /// Overall trust escalation level.
    /// Red if any check fails; Amber if all pass but warnings exist; Green otherwise.
    /// </summary>
    public TrustEscalation EscalationLevel =>
        !SignatureValid || !IssuerTrusted || !NotRevoked || !NotExpired || !RequiredClaimsPresent
            ? TrustEscalation.Red
            : Warnings.Count > 0
                ? TrustEscalation.Amber
                : TrustEscalation.Green;

    /// <summary>Number of the five checks that passed.</summary>
    public int PassedCheckCount =>
        (SignatureValid ? 1 : 0)
        + (IssuerTrusted ? 1 : 0)
        + (NotRevoked ? 1 : 0)
        + (NotExpired ? 1 : 0)
        + (RequiredClaimsPresent ? 1 : 0);

    /// <summary>Total number of verification checks (always 5).</summary>
    public int TotalCheckCount => 5;
}
