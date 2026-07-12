// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US4/US5 — typed org-certificate error codes (data-model §6). Each maps 1:1 to a distinct,
/// actionable failure mode (FR-019/FR-020/FR-024).
/// </summary>
public static class CertErrorCodes
{
    /// <summary>No P-256 key can be resolved for the org — not eligible for the X.509 rail (FR-024).</summary>
    public const string KeyNotEligible = "CERT_KEY_NOT_ELIGIBLE";

    /// <summary>Imported leaf's public key does not match the org's resolved P-256 key (FR-019).</summary>
    public const string KeyMismatch = "CERT_KEY_MISMATCH";

    /// <summary>Imported chain is not internally consistent / cannot be assembled (FR-019).</summary>
    public const string ChainInvalid = "CERT_CHAIN_INVALID";

    /// <summary>Imported leaf or an intermediate is outside its validity window (FR-019).</summary>
    public const string Expired = "CERT_EXPIRED";

    /// <summary>Imported leaf is not suitable for credential signing (KeyUsage excludes signing) (FR-019).</summary>
    public const string Unsuitable = "CERT_UNSUITABLE";

    /// <summary>Issuance requested the external anchor but no valid imported cert exists (FR-020).</summary>
    public const string ExternalAnchorUnavailable = "CERT_EXTERNAL_ANCHOR_UNAVAILABLE";
}

/// <summary>
/// Feature 181 US5 (T047) — thrown when a certificate operation is attempted for an organisation whose
/// issuing key is not P-256 (not eligible for the X.509 rail). Replaces the raw
/// <see cref="System.Security.Cryptography.CryptographicException"/> ("ASN1 corrupted data") 500 with a
/// typed failure the enrol path maps to <see cref="CertErrorCodes.KeyNotEligible"/> (FR-024).
/// </summary>
public sealed class CertKeyNotEligibleException : Exception
{
    public CertKeyNotEligibleException(string message) : base(message) { }
    public CertKeyNotEligibleException(string message, Exception inner) : base(message, inner) { }
}
