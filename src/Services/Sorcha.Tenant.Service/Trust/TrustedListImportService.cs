// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US3 — imports an ETSI TS 119 612 trusted-list document (LOTL or a member-state
/// trusted list): verifies the enveloped XMLDSig signature, extracts CA/QC granted anchors, and
/// persists a versioned snapshot (R5 / FR-011..FR-013). Signature-verification depth is XMLDSig
/// <em>core</em> validation (tamper protection); full XAdES qualifying-property + LOTL pivot-chain
/// validation are deferred under the operator-attested import model (spec D3).
/// </summary>
public sealed class TrustedListImportService
{
    /// <summary>The ETSI TS 119 612 v2 XML namespace.</summary>
    public const string TslNamespace = "http://uri.etsi.org/02231/v2#";

    /// <summary>Service-type URIs whose granted entries yield issuance-relevant CA anchors (R5, config-extensible).</summary>
    private static readonly HashSet<string> AcceptedServiceTypes = new(StringComparer.Ordinal)
    {
        "http://uri.etsi.org/TrstSvc/Svctype/CA/QC",
        "http://uri.etsi.org/TrstSvc/Svctype/CA/PKC",
    };

    /// <summary>Service-status URIs treated as trustworthy for anchor inclusion (R5, config-extensible).</summary>
    private static readonly HashSet<string> AcceptedServiceStatuses = new(StringComparer.Ordinal)
    {
        "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted",
        "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/recognisedatnationallevel",
    };

    private readonly ITrustedListSnapshotStore _store;
    private readonly TimeProvider _clock;

    static TrustedListImportService()
    {
        // SignedXml ships no ECDSA SignatureDescription — register one so ECDSA-signed lists verify
        // (idempotent process-global registration; EU LOTL is RSA, some national lists are ECDSA).
        CryptoConfig.AddAlgorithm(typeof(EcdsaSha256SignatureDescription),
            EcdsaSha256SignatureDescription.SignatureMethodUri);
    }

    public TrustedListImportService(ITrustedListSnapshotStore store, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Verify + parse + persist a trusted-list document under <paramref name="trustListId"/>.
    /// </summary>
    public async Task<TrustedListImportResult> ImportAsync(
        string trustListId,
        byte[] documentBytes,
        string? sourceUrl,
        Guid importedByPlatformUserId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustListId);
        ArgumentNullException.ThrowIfNull(documentBytes);

        // 1. Well-formedness.
        var xml = StripBom(Encoding.UTF8.GetString(documentBytes));
        XmlDocument xmlDoc;
        try
        {
            xmlDoc = new XmlDocument { PreserveWhitespace = true };
            xmlDoc.LoadXml(xml);
        }
        catch (XmlException ex)
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.Malformed, $"Not well-formed XML: {ex.Message}");
        }

        // 2. Enveloped XMLDSig core verification + signer identity (R5).
        var (signatureValid, signerCert) = VerifyEnvelopedSignature(xmlDoc);
        if (!signatureValid)
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.SignatureInvalid,
                "The trusted list is unsigned or its enveloped XMLDSig signature does not verify.");
        }

        // 3. Scheme information.
        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.Malformed, $"Not well-formed XML: {ex.Message}");
        }

        XNamespace ns = TslNamespace;
        var schemeInfo = xdoc.Root?.Element(ns + "SchemeInformation");
        if (schemeInfo is null)
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.Malformed, "Missing SchemeInformation.");
        }

        if (!long.TryParse(schemeInfo.Element(ns + "TSLSequenceNumber")?.Value, out var sequenceNumber))
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.Malformed, "Missing or non-numeric TSLSequenceNumber.");
        }

        if (!TryParseDate(schemeInfo.Element(ns + "ListIssueDateTime")?.Value, out var listIssueDateTime))
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.Malformed, "Missing or invalid ListIssueDateTime.");
        }

        DateTimeOffset? nextUpdate = TryParseDate(
            schemeInfo.Element(ns + "NextUpdate")?.Element(ns + "dateTime")?.Value, out var nu) ? nu : null;

        var territory = schemeInfo.Element(ns + "SchemeTerritory")?.Value;
        var operatorName = schemeInfo.Element(ns + "SchemeOperatorName")?.Elements(ns + "Name").FirstOrDefault()?.Value;

        // 4. Anchor extraction + summary (FR-012).
        var (anchors, extractionSummary) = ExtractAnchors(xdoc, ns);

        // 5. Sequence-regression guard (FR-013).
        var currentSequence = await _store.GetActiveSequenceAsync(trustListId, ct);
        if (currentSequence is { } current && sequenceNumber <= current)
        {
            return TrustedListImportResult.Fail(TrustListErrorCodes.SequenceRegression,
                $"Sequence {sequenceNumber} is not newer than the current Active snapshot ({current}).");
        }

        // 6. Persist.
        var snapshot = new TrustedListSnapshot
        {
            Id = Guid.NewGuid(),
            TrustListId = trustListId,
            SequenceNumber = sequenceNumber,
            SchemeTerritory = territory,
            SchemeOperatorName = operatorName,
            ListIssueDateTime = listIssueDateTime,
            NextUpdate = nextUpdate,
            SignerCertSubject = signerCert?.Subject ?? "(unknown)",
            SignerCertThumbprint = signerCert is not null
                ? Convert.ToHexString(SHA256.HashData(signerCert.RawData))
                : string.Empty,
            ImportedAt = _clock.GetUtcNow(),
            ImportedByPlatformUserId = importedByPlatformUserId,
            SourceUrl = sourceUrl,
            RawDocumentSha256 = Convert.ToHexString(SHA256.HashData(documentBytes)),
            Status = TrustedListSnapshotStatus.Active,
            Anchors = anchors,
            ExtractionSummary = extractionSummary,
        };
        foreach (var anchor in anchors)
        {
            anchor.SnapshotId = snapshot.Id;
        }

        await _store.AddAsync(snapshot, ct);
        return TrustedListImportResult.Ok(snapshot);
    }

    private static (bool Valid, X509Certificate2? Signer) VerifyEnvelopedSignature(XmlDocument doc)
    {
        var signatureNodes = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (signatureNodes.Count == 0 || signatureNodes[0] is not XmlElement signatureElement)
        {
            return (false, null);
        }

        try
        {
            var signedXml = new SignedXml(doc);
            signedXml.LoadXml(signatureElement);

            var signerCert = signedXml.KeyInfo.OfType<KeyInfoX509Data>()
                .SelectMany(d => d.Certificates?.Cast<X509Certificate2>() ?? [])
                .FirstOrDefault();

            // verifySignatureOnly: true — core tamper check only, not chain-to-trusted-root (R5 / D3).
            var valid = signerCert is not null
                ? signedXml.CheckSignature(signerCert, verifySignatureOnly: true)
                : signedXml.CheckSignature();

            return (valid, signerCert);
        }
        catch (CryptographicException)
        {
            return (false, null);
        }
    }

    private static (List<TrustedListAnchor> Anchors, string Summary) ExtractAnchors(XDocument xdoc, XNamespace ns)
    {
        var anchors = new List<TrustedListAnchor>();
        var skipped = new List<object>();

        foreach (var service in xdoc.Descendants(ns + "TSPService"))
        {
            var info = service.Element(ns + "ServiceInformation");
            var type = info?.Element(ns + "ServiceTypeIdentifier")?.Value ?? string.Empty;
            var status = info?.Element(ns + "ServiceStatus")?.Value ?? string.Empty;

            if (!AcceptedServiceTypes.Contains(type) || !AcceptedServiceStatuses.Contains(status))
            {
                skipped.Add(new { serviceType = type, serviceStatus = status, reason = "type/status not accepted for issuance anchors" });
                continue;
            }

            var certB64 = info?.Element(ns + "ServiceDigitalIdentity")?
                .Element(ns + "DigitalId")?
                .Element(ns + "X509Certificate")?.Value;
            if (string.IsNullOrWhiteSpace(certB64))
            {
                skipped.Add(new { serviceType = type, serviceStatus = status, reason = "no X509Certificate digital identity" });
                continue;
            }

            try
            {
                var der = Convert.FromBase64String(certB64.Trim());
                var cert = X509CertificateLoader.LoadCertificate(der);
                anchors.Add(new TrustedListAnchor
                {
                    Id = Guid.NewGuid(),
                    CertificateDer = der,
                    SubjectDn = cert.Subject,
                    Thumbprint = Convert.ToHexString(SHA256.HashData(der)),
                    ServiceTypeIdentifier = type,
                    ServiceStatus = status,
                    NotBefore = cert.NotBefore,
                    NotAfter = cert.NotAfter,
                });
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                skipped.Add(new { serviceType = type, serviceStatus = status, reason = $"unparseable certificate: {ex.Message}" });
            }
        }

        var summary = JsonSerializer.Serialize(new
        {
            extracted = anchors.Count,
            skipped = skipped.Count,
            skippedEntries = skipped,
        });
        return (anchors, summary);
    }

    private static bool TryParseDate(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }

    private static string StripBom(string s) => s.Length > 0 && s[0] == '﻿' ? s[1..] : s;
}

/// <summary>Outcome of a trusted-list import — a persisted snapshot, or a typed failure (data-model §6).</summary>
/// <param name="Success">Whether the import succeeded.</param>
/// <param name="ErrorCode">Typed failure code from <see cref="TrustListErrorCodes"/> when unsuccessful.</param>
/// <param name="ErrorMessage">Human-readable failure detail.</param>
/// <param name="Snapshot">The persisted snapshot on success.</param>
public sealed record TrustedListImportResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    TrustedListSnapshot? Snapshot)
{
    /// <summary>A successful import carrying the persisted snapshot.</summary>
    public static TrustedListImportResult Ok(TrustedListSnapshot snapshot) => new(true, null, null, snapshot);

    /// <summary>A typed import failure.</summary>
    public static TrustedListImportResult Fail(string code, string message) => new(false, code, message, null);
}
