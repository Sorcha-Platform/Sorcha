// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Feature 181 US5 (T050) — org-admin client for the X.509 certificate lifecycle surface
/// (<c>/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}</c> via the gateway). Lists the
/// org's internal + imported certificates with status/validity/chain summary and eligibility, and
/// drives re-issue (enrol), CSR generation, external-cert import, and retire/delete. Mirrors
/// <see cref="ITrustedListAdminService"/> in shape (typed HttpClient, record DTOs, <c>*Outcome</c>
/// result types for the 422-carrying calls).
/// The host registers this typed client with <c>AuthenticatedHttpMessageHandler</c>, which attaches the admin's bearer token — there is no browser session to ride: the WASM client sends no cookie, and these endpoints are admin + platform-audience gated. Registered without that handler, every call 401s (and the swallowed failure renders as a plausible "nothing here yet" empty state).
/// </summary>
public interface IOrgCertificateAdminService
{
    /// <summary>List the org's certificates plus the X.509-rail eligibility verdict.</summary>
    Task<OrgCertificatesResult> ListAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default);

    /// <summary>Generate a CSR bound to the org's P-256 issuing key (422 CERT_KEY_NOT_ELIGIBLE).</summary>
    Task<OrgCsrOutcome> GenerateCsrAsync(string tenantId, string orgWalletAddress, string? subjectDn, CancellationToken ct = default);

    /// <summary>Import an externally-issued leaf certificate (+ optional chain).</summary>
    Task<OrgCertificateOutcome> ImportAsync(string tenantId, string orgWalletAddress, string certificatePem, IReadOnlyList<string>? chainPem, CancellationToken ct = default);

    /// <summary>Re-issue (or backfill) the org's internal certificate signed by the tenant root.</summary>
    Task<OrgCertificateOutcome> EnrolAsync(string tenantId, string orgWalletAddress, string? orgDisplayName, CancellationToken ct = default);

    /// <summary>Retire an imported certificate. Returns whether the request succeeded.</summary>
    Task<bool> DeleteAsync(string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default);
}

/// <summary>Client view of a single org certificate (mirrors the server <c>OrgCertificateView</c>).</summary>
public sealed record OrgCertificateSummary
{
    public Guid Id { get; init; }
    public string Provenance { get; init; } = "Internal";
    public string Status { get; init; } = "Active";
    public string SerialNumber { get; init; } = string.Empty;
    public string SubjectDn { get; init; } = string.Empty;
    public string San { get; init; } = string.Empty;
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
    public string BoundKeySource { get; init; } = string.Empty;
    public IReadOnlyList<string> ChainSummary { get; init; } = [];
    public string CertificateBase64 { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>True for externally-imported certificates (Delete/retire applies only to these).</summary>
    public bool IsImported => string.Equals(Provenance, "Imported", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the certificate is Active (drives the status chip colour).</summary>
    public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    /// <summary>True once past the not-after instant.</summary>
    public bool IsExpired => NotAfter <= DateTimeOffset.UtcNow;

    /// <summary>True when the certificate expires within ~30 days (drives the expiry-warning chip).</summary>
    public bool IsExpiringSoon => !IsExpired && NotAfter <= DateTimeOffset.UtcNow.AddDays(30);
}

/// <summary>Client view of the X.509-rail eligibility verdict.</summary>
public sealed record OrgCertEligibilityView
{
    public bool Eligible { get; init; }
    public string? Reason { get; init; }
    public string? BoundKeySource { get; init; }
}

/// <summary>The org's certificate list plus eligibility (GET /certificates).</summary>
public sealed record OrgCertificatesResult
{
    public OrgCertEligibilityView Eligibility { get; init; } = new();
    public IReadOnlyList<OrgCertificateSummary> Certificates { get; init; } = [];
}

/// <summary>Client view of a generated CSR (POST /csr success body).</summary>
public sealed record OrgCsrResult
{
    public string CsrPem { get; init; } = string.Empty;
    public string BoundKeySource { get; init; } = string.Empty;
    public string BoundPublicKeyThumbprint { get; init; } = string.Empty;
}

/// <summary>Result of a CSR generation attempt — the PEM, or a typed failure (422 CERT_KEY_NOT_ELIGIBLE).</summary>
public sealed record OrgCsrOutcome(bool Success, string? ErrorCode, string? ErrorMessage, OrgCsrResult? Result)
{
    public static OrgCsrOutcome Ok(OrgCsrResult result) => new(true, null, null, result);
    public static OrgCsrOutcome Fail(string? code, string? message) => new(false, code, message, null);
}

/// <summary>Result of an import / enrol attempt — the stored certificate, or a typed failure.</summary>
public sealed record OrgCertificateOutcome(bool Success, string? ErrorCode, string? ErrorMessage, OrgCertificateSummary? Certificate)
{
    public static OrgCertificateOutcome Ok(OrgCertificateSummary certificate) => new(true, null, null, certificate);
    public static OrgCertificateOutcome Fail(string? code, string? message) => new(false, code, message, null);
}
