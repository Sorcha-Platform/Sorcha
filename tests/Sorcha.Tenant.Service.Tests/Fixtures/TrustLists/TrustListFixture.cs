// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

namespace Sorcha.Tenant.Service.Tests.Fixtures.TrustLists;

/// <summary>
/// Options controlling the shape of a generated ETSI TS 119 612 trusted list.
/// </summary>
/// <param name="SequenceNumber">Value for <c>TSLSequenceNumber</c>.</param>
/// <param name="ListIssueDateTime">Value for <c>ListIssueDateTime</c>.</param>
/// <param name="NextUpdate">Optional value for <c>NextUpdate/dateTime</c>; omitted when null.</param>
/// <param name="SchemeTerritory">Two-letter scheme territory (e.g. "IE").</param>
/// <param name="SchemeOperatorName">Human-readable scheme operator name.</param>
/// <param name="Services">Trust service entries to embed in the list.</param>
public sealed record TrustListOptions(
    long SequenceNumber,
    DateTimeOffset ListIssueDateTime,
    DateTimeOffset? NextUpdate,
    string SchemeTerritory,
    string SchemeOperatorName,
    IReadOnlyList<TrustListServiceEntry> Services);

/// <summary>
/// A single trust service entry inside a generated trusted list.
/// </summary>
/// <param name="ServiceTypeIdentifier">The <c>ServiceTypeIdentifier</c> URI.</param>
/// <param name="ServiceStatus">The <c>ServiceStatus</c> URI.</param>
/// <param name="Certificate">Certificate embedded as DER base64 in <c>ServiceDigitalIdentity/DigitalId/X509Certificate</c>.</param>
/// <param name="ServiceName">Human-readable service name.</param>
public sealed record TrustListServiceEntry(
    string ServiceTypeIdentifier,
    string ServiceStatus,
    X509Certificate2 Certificate,
    string ServiceName = "Sorcha Test Trust Service");

/// <summary>
/// Test fixture for ETSI TS 119 612 trusted-list scenarios (Feature 181 trust rail).
/// Provides an in-memory P-256 test CA, leaf issuance, a minimal
/// <c>TrustServiceStatusList</c> XML builder, enveloped XMLDSig signing with a
/// distinct scheme-operator certificate, and a tamper helper for negative tests.
/// </summary>
public sealed class TrustListFixture : IDisposable
{
    /// <summary>The ETSI TS 119 612 v2 XML namespace.</summary>
    public const string TslNamespace = "http://uri.etsi.org/02231/v2#";

    /// <summary>Default qualified-CA service type identifier.</summary>
    public const string CaQcServiceType = "http://uri.etsi.org/TrstSvc/Svctype/CA/QC";

    /// <summary>Default "granted" service status URI.</summary>
    public const string GrantedServiceStatus = "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted";

    /// <summary>The XMLDSig signature-method URI for ECDSA P-256 / SHA-256 (RFC 4051).</summary>
    public const string EcdsaSha256SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";

    private readonly ECDsa _caKey;
    private readonly ECDsa _schemeOperatorKey;
    private bool _disposed;

    static TrustListFixture()
    {
        // SignedXml has no built-in SignatureDescription for ECDSA — without this,
        // ComputeSignature/CheckSignature throw "SignatureDescription could not be created".
        CryptoConfig.AddAlgorithm(typeof(EcdsaSha256SignatureDescription), EcdsaSha256SignatureMethod);
    }

    /// <summary>
    /// Creates a fresh fixture with a new test CA and scheme-operator certificate.
    /// Certificates are stable for the lifetime of the fixture instance.
    /// </summary>
    public TrustListFixture()
    {
        _caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TestCa = CreateCaCertificate(_caKey);

        _schemeOperatorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SchemeOperatorCert = CreateSchemeOperatorCertificate(_schemeOperatorKey);
    }

    /// <summary>Self-signed P-256 test CA ("CN=Sorcha Test Trust CA") with private key.</summary>
    public X509Certificate2 TestCa { get; }

    /// <summary>P-256 scheme-operator signing certificate (distinct from <see cref="TestCa"/>) with private key.</summary>
    public X509Certificate2 SchemeOperatorCert { get; }

    /// <summary>
    /// Issues an end-entity leaf certificate signed by <see cref="TestCa"/>.
    /// </summary>
    /// <param name="subjectCn">Common name for the leaf subject (without the "CN=" prefix).</param>
    /// <param name="leafKey">The leaf's P-256 key pair; the returned certificate carries only the public key.</param>
    public X509Certificate2 IssueLeaf(string subjectCn, ECDsa leafKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectCn);
        ArgumentNullException.ThrowIfNull(leafKey);

        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={subjectCn}"),
            leafKey,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F; // keep serial positive

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        return request.Create(TestCa, notBefore, notBefore.AddYears(2), serial);
    }

    /// <summary>
    /// Returns default options: one granted CA/QC entry backed by <see cref="TestCa"/>,
    /// sequence number 1, issued now, next update in 6 months, territory "IE".
    /// </summary>
    public TrustListOptions DefaultOptions(long sequenceNumber = 1) => new(
        SequenceNumber: sequenceNumber,
        ListIssueDateTime: DateTimeOffset.UtcNow,
        NextUpdate: DateTimeOffset.UtcNow.AddMonths(6),
        SchemeTerritory: "IE",
        SchemeOperatorName: "Sorcha Test Scheme Operator",
        Services:
        [
            new TrustListServiceEntry(CaQcServiceType, GrantedServiceStatus, TestCa, "Sorcha Test Trust CA")
        ]);

    /// <summary>
    /// Builds a minimal (unsigned) ETSI TS 119 612 <c>TrustServiceStatusList</c> document.
    /// </summary>
    public string BuildTrustListXml(TrustListOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        XNamespace ns = TslNamespace;
        XNamespace xml = XNamespace.Xml;

        var schemeInformation = new XElement(ns + "SchemeInformation",
            new XElement(ns + "TSLVersionIdentifier", 5),
            new XElement(ns + "TSLSequenceNumber", options.SequenceNumber),
            new XElement(ns + "TSLType", "http://uri.etsi.org/TrstSvc/TrustedList/TSLType/EUgeneric"),
            new XElement(ns + "SchemeOperatorName",
                new XElement(ns + "Name", new XAttribute(xml + "lang", "en"), options.SchemeOperatorName)),
            new XElement(ns + "SchemeTerritory", options.SchemeTerritory),
            new XElement(ns + "ListIssueDateTime", FormatDateTime(options.ListIssueDateTime)));

        if (options.NextUpdate is { } nextUpdate)
        {
            schemeInformation.Add(new XElement(ns + "NextUpdate",
                new XElement(ns + "dateTime", FormatDateTime(nextUpdate))));
        }

        var tspServices = new XElement(ns + "TSPServices");
        foreach (var service in options.Services)
        {
            tspServices.Add(new XElement(ns + "TSPService",
                new XElement(ns + "ServiceInformation",
                    new XElement(ns + "ServiceTypeIdentifier", service.ServiceTypeIdentifier),
                    new XElement(ns + "ServiceName",
                        new XElement(ns + "Name", new XAttribute(xml + "lang", "en"), service.ServiceName)),
                    new XElement(ns + "ServiceDigitalIdentity",
                        new XElement(ns + "DigitalId",
                            new XElement(ns + "X509Certificate",
                                Convert.ToBase64String(service.Certificate.RawData)))),
                    new XElement(ns + "ServiceStatus", service.ServiceStatus),
                    new XElement(ns + "StatusStartingTime", FormatDateTime(options.ListIssueDateTime)))));
        }

        var root = new XElement(ns + "TrustServiceStatusList",
            new XAttribute("TSLTag", "http://uri.etsi.org/19612/TSLTag"),
            new XAttribute("Id", "TrustServiceStatusList"),
            schemeInformation,
            new XElement(ns + "TrustServiceProviderList",
                new XElement(ns + "TrustServiceProvider",
                    new XElement(ns + "TSPInformation",
                        new XElement(ns + "TSPName",
                            new XElement(ns + "Name", new XAttribute(xml + "lang", "en"), options.SchemeOperatorName))),
                    tspServices)));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root)
            .ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Signs a trusted-list document with an enveloped XMLDSig signature (ECDSA P-256 / SHA-256)
    /// using <see cref="SchemeOperatorCert"/>, embedding the certificate in KeyInfo (X509Data).
    /// </summary>
    public string SignTrustListXml(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        var signedXml = new SignedXml(doc)
        {
            SigningKey = _schemeOperatorKey,
        };
        signedXml.SignedInfo!.SignatureMethod = EcdsaSha256SignatureMethod;
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

        var reference = new Reference("");
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(SchemeOperatorCert));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var signatureElement = signedXml.GetXml();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signatureElement, deep: true));

        return doc.OuterXml;
    }

    /// <summary>Builds and signs a trusted list in one step.</summary>
    public string BuildSignedTrustList(TrustListOptions options) =>
        SignTrustListXml(BuildTrustListXml(options));

    /// <summary>
    /// Verifies the enveloped XMLDSig signature of a signed trusted-list document.
    /// </summary>
    public static bool VerifySignature(string signedXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedXml);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signedXml);

        var signatureNodes = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (signatureNodes.Count == 0 || signatureNodes[0] is not XmlElement signatureElement)
        {
            return false;
        }

        var verifier = new SignedXml(doc);
        verifier.LoadXml(signatureElement);
        return verifier.CheckSignature();
    }

    /// <summary>
    /// Flips one character of the signed content (the <c>SchemeTerritory</c> value)
    /// so that signature verification fails. The result remains well-formed XML.
    /// </summary>
    public string Tamper(string signedXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedXml);

        const string marker = "<SchemeTerritory>";
        var index = signedXml.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("Signed XML does not contain a SchemeTerritory element to tamper with.");
        }

        var charIndex = index + marker.Length;
        var original = signedXml[charIndex];
        var flipped = original == 'X' ? 'Y' : 'X';
        return signedXml[..charIndex] + flipped + signedXml[(charIndex + 1)..];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TestCa.Dispose();
        SchemeOperatorCert.Dispose();
        _caKey.Dispose();
        _schemeOperatorKey.Dispose();
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static X509Certificate2 CreateCaCertificate(ECDsa key)
    {
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=Sorcha Test Trust CA"),
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        return request.CreateSelfSigned(notBefore, notBefore.AddYears(10));
    }

    private static X509Certificate2 CreateSchemeOperatorCertificate(ECDsa key)
    {
        var request = new CertificateRequest(
            new X500DistinguishedName("CN=Sorcha Test Scheme Operator"),
            key,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        return request.CreateSelfSigned(notBefore, notBefore.AddYears(10));
    }
}

/// <summary>
/// XMLDSig signature description mapping the RFC 4051 ecdsa-sha256 URI onto
/// <see cref="ECDsa"/> sign/verify, since .NET ships no built-in ECDSA description.
/// Public because <see cref="CryptoConfig.AddAlgorithm(Type, string[])"/> requires a visible type.
/// </summary>
public sealed class EcdsaSha256SignatureDescription : SignatureDescription
{
    /// <summary>Initialises the description with the ECDsa key algorithm.</summary>
    public EcdsaSha256SignatureDescription()
    {
        KeyAlgorithm = typeof(ECDsa).AssemblyQualifiedName;
    }

    /// <inheritdoc />
    public override HashAlgorithm CreateDigest() => SHA256.Create();

    /// <inheritdoc />
    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key) =>
        new EcdsaSignatureFormatter((ECDsa)key);

    /// <inheritdoc />
    public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key) =>
        new EcdsaSignatureDeformatter((ECDsa)key);

    private sealed class EcdsaSignatureFormatter(ECDsa key) : AsymmetricSignatureFormatter
    {
        private ECDsa _key = key;

        public override byte[] CreateSignature(byte[] rgbHash) => _key.SignHash(rgbHash);

        public override void SetHashAlgorithm(string strName)
        {
            // Hash algorithm is fixed to SHA-256 by the signature description.
        }

        public override void SetKey(AsymmetricAlgorithm key) => _key = (ECDsa)key;
    }

    private sealed class EcdsaSignatureDeformatter(ECDsa key) : AsymmetricSignatureDeformatter
    {
        private ECDsa _key = key;

        public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature) =>
            _key.VerifyHash(rgbHash, rgbSignature);

        public override void SetHashAlgorithm(string strName)
        {
            // Hash algorithm is fixed to SHA-256 by the signature description.
        }

        public override void SetKey(AsymmetricAlgorithm key) => _key = (ECDsa)key;
    }
}
