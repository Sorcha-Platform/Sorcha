// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US4 — the org-certificate lifecycle: eligibility, CSR generation, external-certificate
/// import validation (FR-019), supersede semantics, and the key-mismatch flag on rotation. The org's
/// P-256 issuing key is resolved via the Wallet Service (primary ES256 else HAIP co-key — research R9);
/// the private key never leaves Wallet custody (research R10).
/// </summary>
public interface IOrgCertificateService
{
    /// <summary>Whether a P-256 key resolves for the org (X.509-rail eligibility, FR-024).</summary>
    Task<OrgCertEligibility> GetEligibilityAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default);

    /// <summary>Generate a CSR bound to the org's P-256 key (FR-018). Fails typed when ineligible.</summary>
    Task<CsrResult> GenerateCsrAsync(
        string tenantId, string orgWalletAddress, string? subjectDn, Guid createdBy, CancellationToken ct = default);

    /// <summary>Validate + import an externally-issued certificate + chain (FR-019), superseding any prior import.</summary>
    Task<CertImportResult> ImportAsync(
        string tenantId, string orgWalletAddress, byte[] leafDer, IReadOnlyList<byte[]> chainDer,
        Guid createdBy, CancellationToken ct = default);

    /// <summary>Every certificate for an org (internal + imported), newest first.</summary>
    Task<IReadOnlyList<OrgCertificateRecord>> ListAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default);

    /// <summary>Retire an imported certificate (Status→Superseded). Idempotent.</summary>
    Task<bool> RetireImportedAsync(
        string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default);

    /// <summary>
    /// Resolve the org's Active imported chain for issuance on the external anchor (T044). Fails closed
    /// with a typed code when absent, expired, or key-mismatched after rotation (FR-020) — never falls back.
    /// A live key-mismatch also flags the stored record <see cref="OrgCertificateStatus.KeyMismatch"/>.
    /// </summary>
    Task<ImportedChainResult> ResolveActiveImportedChainAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default);
}

/// <summary>X.509-rail eligibility verdict for an org.</summary>
public sealed record OrgCertEligibility(bool Eligible, string? Reason, OrgCertificateKeySource? BoundKeySource);

/// <summary>Result of a CSR generation.</summary>
public sealed record CsrResult(
    bool Success, string? ErrorCode, string? CsrPem,
    OrgCertificateKeySource? BoundKeySource, string? BoundPublicKeyThumbprint);

/// <summary>Result of a certificate import.</summary>
public sealed record CertImportResult(
    bool Success, string? ErrorCode, string? ErrorMessage, OrgCertificateRecord? Certificate);

/// <summary>Result of resolving the active imported chain for issuance.</summary>
public sealed record ImportedChainResult(bool Available, string? ErrorCode, IReadOnlyList<byte[]>? Chain);

/// <inheritdoc />
public sealed class OrgCertificateService : IOrgCertificateService
{
    private readonly ICertificateStore _store;
    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<OrgCertificateService> _logger;

    public OrgCertificateService(
        ICertificateStore store,
        IWalletServiceClient walletClient,
        ILogger<OrgCertificateService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OrgCertEligibility> GetEligibilityAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default)
    {
        var r = await _walletClient.ResolveIssuerCertKeyAsync(orgWalletAddress, ct);
        return r.Eligible
            ? new OrgCertEligibility(true, null, MapKeySource(r.BoundKeySource))
            : new OrgCertEligibility(false, r.Reason ?? CertErrorCodes.KeyNotEligible, null);
    }

    /// <inheritdoc />
    public async Task<CsrResult> GenerateCsrAsync(
        string tenantId, string orgWalletAddress, string? subjectDn, Guid createdBy, CancellationToken ct = default)
    {
        var resolve = await _walletClient.ResolveIssuerCertKeyAsync(orgWalletAddress, ct);
        if (!resolve.Eligible || resolve.PublicKeySpki is null)
        {
            OrgCertMetrics.RecordIssuance("internal", "failure", CertErrorCodes.KeyNotEligible);
            return new CsrResult(false, CertErrorCodes.KeyNotEligible, null, null, null);
        }

        var dn = string.IsNullOrWhiteSpace(subjectDn)
            ? $"CN={orgWalletAddress}, O=Sorcha Org, C=IE"
            : subjectDn!;

        using var ecdsaPub = ECDsa.Create();
        ecdsaPub.ImportSubjectPublicKeyInfo(resolve.PublicKeySpki, out _);

        var request = new CertificateRequest(new X500DistinguishedName(dn), ecdsaPub, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

        var generator = new WalletBackedSignatureGenerator(_walletClient, orgWalletAddress, resolve.PublicKeySpki);
        var csrDer = request.CreateSigningRequest(generator);
        var csrPem = PemEncode("CERTIFICATE REQUEST", csrDer);
        var thumbprint = Convert.ToHexString(SHA256.HashData(resolve.PublicKeySpki));

        await _store.AddCsrAsync(new CsrRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrgWalletAddress = orgWalletAddress,
            CsrPem = csrPem,
            BoundPublicKeySpki = resolve.PublicKeySpki,
            BoundKeySource = MapKeySource(resolve.BoundKeySource) ?? OrgCertificateKeySource.HaipCoKey,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByPlatformUserId = createdBy,
        }, ct);

        _logger.LogInformation("Generated CSR for org {Org} under tenant {Tenant} (boundKeySource={Source})",
            orgWalletAddress, tenantId, resolve.BoundKeySource);
        OrgCertMetrics.RecordIssuance("internal", "success", "csr");

        return new CsrResult(true, null, csrPem, MapKeySource(resolve.BoundKeySource), thumbprint);
    }

    /// <inheritdoc />
    public async Task<CertImportResult> ImportAsync(
        string tenantId, string orgWalletAddress, byte[] leafDer, IReadOnlyList<byte[]> chainDer,
        Guid createdBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(leafDer);
        chainDer ??= [];

        // (0) org key must be resolvable.
        var resolve = await _walletClient.ResolveIssuerCertKeyAsync(orgWalletAddress, ct);
        if (!resolve.Eligible || resolve.PublicKeySpki is null)
        {
            return Fail(CertErrorCodes.KeyNotEligible, "Org has no resolvable P-256 key.");
        }

        X509Certificate2 leaf;
        try
        {
            leaf = X509CertificateLoader.LoadCertificate(leafDer);
        }
        catch (CryptographicException ex)
        {
            return Fail(CertErrorCodes.ChainInvalid, $"Leaf certificate could not be parsed: {ex.Message}");
        }

        static CertImportResult Fail(string code, string message)
        {
            OrgCertMetrics.RecordIssuance("imported", "failure", code);
            return new CertImportResult(false, code, message, null);
        }

        using (leaf)
        {
            var uploaded = new List<X509Certificate2>();
            try
            {
                foreach (var der in chainDer)
                {
                    uploaded.Add(X509CertificateLoader.LoadCertificate(der));
                }

                // (a) key match — leaf SPKI byte-for-byte against the org's resolved P-256 key.
                var leafSpki = leaf.PublicKey.ExportSubjectPublicKeyInfo();
                if (!leafSpki.SequenceEqual(resolve.PublicKeySpki))
                {
                    return Fail(CertErrorCodes.KeyMismatch, "Certificate public key does not match the org's issuing key.");
                }

                // (c) validity window — leaf.
                var now = DateTimeOffset.UtcNow;
                if (now < leaf.NotBefore || now > leaf.NotAfter)
                {
                    return Fail(CertErrorCodes.Expired, "Certificate is outside its validity window.");
                }

                // (d) suitability — KeyUsage, if present, must include digitalSignature (absent passes).
                var ku = leaf.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
                if (ku is not null && !ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
                {
                    return Fail(CertErrorCodes.Unsuitable, "Certificate KeyUsage does not permit digitalSignature.");
                }

                // (b) chain build over the uploaded set (with-root and without-root both normalise).
                var ordered = TryBuildChain(leaf, uploaded, now, out var chainError);
                if (ordered is null)
                {
                    return Fail(chainError ?? CertErrorCodes.ChainInvalid,
                        "Certificate chain is not internally consistent or is outside validity.");
                }

                var record = new OrgCertificateRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OrgWalletAddress = orgWalletAddress,
                    Provenance = OrgCertificateProvenance.Imported,
                    Status = OrgCertificateStatus.Active,
                    CertificateDer = leaf.RawData,
                    ChainDer = ordered, // leaf-exclusive ordered chain (leaf→root)
                    BoundPublicKeySpki = resolve.PublicKeySpki,
                    BoundKeySource = MapKeySource(resolve.BoundKeySource) ?? OrgCertificateKeySource.HaipCoKey,
                    SerialNumber = leaf.SerialNumber,
                    SubjectDn = leaf.Subject,
                    San = ExtractSan(leaf),
                    NotBefore = leaf.NotBefore,
                    NotAfter = leaf.NotAfter,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedByPlatformUserId = createdBy,
                };
                await _store.AddAsync(record, ct);

                _logger.LogInformation(
                    "Imported external certificate for org {Org} under tenant {Tenant}: serial={Serial} chainLen={Len}",
                    orgWalletAddress, tenantId, leaf.SerialNumber, ordered.Count);
                OrgCertMetrics.RecordIssuance("imported", "success", "ok");

                return new CertImportResult(true, null, null, record);
            }
            finally
            {
                foreach (var c in uploaded)
                {
                    c.Dispose();
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrgCertificateRecord>> ListAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default)
        => _store.ListForOrgAsync(tenantId, orgWalletAddress, ct);

    /// <inheritdoc />
    public async Task<bool> RetireImportedAsync(
        string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default)
    {
        var cert = await _store.GetByIdAsync(tenantId, orgWalletAddress, certificateId, ct);
        if (cert is null || cert.Provenance != OrgCertificateProvenance.Imported)
        {
            return false;
        }
        if (cert.Status == OrgCertificateStatus.Superseded)
        {
            return true; // idempotent
        }
        cert.Status = OrgCertificateStatus.Superseded;
        await _store.UpdateAsync(cert, ct);
        _logger.LogInformation("Retired imported certificate {Id} for org {Org}", certificateId, orgWalletAddress);
        return true;
    }

    /// <inheritdoc />
    public async Task<ImportedChainResult> ResolveActiveImportedChainAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default)
    {
        var cert = await _store.GetActiveAsync(tenantId, orgWalletAddress, OrgCertificateProvenance.Imported, ct);
        if (cert is null)
        {
            return new ImportedChainResult(false, CertErrorCodes.ExternalAnchorUnavailable, null);
        }

        var now = DateTimeOffset.UtcNow;
        if (now < cert.NotBefore || now > cert.NotAfter)
        {
            return new ImportedChainResult(false, CertErrorCodes.ExternalAnchorUnavailable, null);
        }

        // Rotation check — the bound key must still match the org's current resolved P-256 key.
        var resolve = await _walletClient.ResolveIssuerCertKeyAsync(orgWalletAddress, ct);
        if (!resolve.Eligible || resolve.PublicKeySpki is null || !resolve.PublicKeySpki.SequenceEqual(cert.BoundPublicKeySpki))
        {
            if (cert.Status == OrgCertificateStatus.Active)
            {
                cert.Status = OrgCertificateStatus.KeyMismatch;
                await _store.UpdateAsync(cert, ct);
                _logger.LogWarning(
                    "Imported certificate {Id} for org {Org} flagged KeyMismatch — org key rotated away from the bound cert key.",
                    cert.Id, orgWalletAddress);
            }
            return new ImportedChainResult(false, CertErrorCodes.ExternalAnchorUnavailable, null);
        }

        // JWS x5c wants leaf-first, root-last: leaf + stored leaf-exclusive chain.
        var full = new List<byte[]>(cert.ChainDer.Count + 1) { cert.CertificateDer };
        full.AddRange(cert.ChainDer);
        return new ImportedChainResult(true, null, full);
    }

    /// <summary>
    /// Assemble the ordered leaf-exclusive chain (leaf→root) by following issuer links through the uploaded
    /// set, tolerating a missing external root (chain-without-root) but rejecting a genuinely broken chain.
    /// Uses <see cref="X509Chain"/> with the uploaded set as extra store and any self-signed root as a custom
    /// trust anchor; unknown-authority / partial-chain is accepted, signature/time faults are not.
    /// </summary>
    private static List<byte[]>? TryBuildChain(
        X509Certificate2 leaf, IReadOnlyList<X509Certificate2> uploaded, DateTimeOffset now, out string? errorCode)
    {
        errorCode = null;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.VerificationFlags =
            X509VerificationFlags.AllowUnknownCertificateAuthority
            | X509VerificationFlags.IgnoreEndRevocationUnknown
            | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown;

        var root = uploaded.FirstOrDefault(c => c.SubjectName.RawData.SequenceEqual(c.IssuerName.RawData));
        if (root is not null)
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
        }
        foreach (var c in uploaded)
        {
            chain.ChainPolicy.ExtraStore.Add(c);
        }

        chain.Build(leaf); // return value ignored — inspect status flags for real faults.

        foreach (var status in chain.ChainStatus)
        {
            switch (status.Status)
            {
                case X509ChainStatusFlags.NoError:
                case X509ChainStatusFlags.UntrustedRoot:
                case X509ChainStatusFlags.PartialChain:
                case X509ChainStatusFlags.RevocationStatusUnknown:
                case X509ChainStatusFlags.OfflineRevocation:
                    break;
                case X509ChainStatusFlags.NotTimeValid:
                case X509ChainStatusFlags.NotTimeNested:
                    errorCode = CertErrorCodes.Expired;
                    return null;
                default:
                    errorCode = CertErrorCodes.ChainInvalid;
                    return null;
            }
        }

        // Ordered leaf-exclusive chain from the assembled elements (leaf is element 0).
        var ordered = new List<byte[]>();
        for (var i = 1; i < chain.ChainElements.Count; i++)
        {
            var cert = chain.ChainElements[i].Certificate;
            if (now < cert.NotBefore || now > cert.NotAfter)
            {
                errorCode = CertErrorCodes.Expired;
                return null;
            }
            ordered.Add(cert.RawData);
        }
        return ordered;
    }

    private static OrgCertificateKeySource? MapKeySource(string? source) => source switch
    {
        "Primary" => OrgCertificateKeySource.Primary,
        "HaipCoKey" => OrgCertificateKeySource.HaipCoKey,
        _ => null,
    };

    private static string ExtractSan(X509Certificate2 leaf)
    {
        var san = leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san is null)
        {
            return leaf.Subject;
        }
        try
        {
            var dns = san.EnumerateDnsNames().FirstOrDefault();
            if (!string.IsNullOrEmpty(dns))
            {
                return dns!;
            }
        }
        catch (CryptographicException)
        {
            // fall through to the formatted SAN
        }
        return san.Format(false);
    }

    private static string PemEncode(string label, byte[] der)
        => new string(PemEncoding.Write(label, der));
}
