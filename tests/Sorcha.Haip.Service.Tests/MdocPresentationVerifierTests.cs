// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.Cose;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Mdoc;
using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;
using Sorcha.Haip.Service.Services;
using Xunit;

using EngineStatus = Sorcha.Blueprint.Engine.Credentials.CredentialStatusValue;

namespace Sorcha.Haip.Service.Tests;

/// <summary>
/// Feature 135 / T038 — the HAIP mso_mdoc verifier accepts a valid PID-shape presentation that
/// chains to a trusted anchor, and fails closed on an untrusted issuer (chain), tampered element
/// (integrity), bad device binding (holder binding), and a revoked status. The issuer cert is
/// self-signed, so trusting it == placing it in the x509-tenant anchor set.
/// </summary>
public class MdocPresentationVerifierTests
{
    private const string ClientId = "x509_san_dns:verifier.example.com";
    private const string Nonce = "mdoc-haip-nonce";
    private const string ResponseUri = "https://verifier.example.com/response";
    private const string DocType = "eu.europa.ec.eudi.pid.1";

    private sealed class FakeAnchors(byte[]? root) : ITenantTrustAnchorProvider
    {
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default) =>
            Task.FromResult<TrustAnchorSet?>(root is null ? null : new TrustAnchorSet { Roots = [root], CheckRevocation = false });
    }

    private sealed class FakeStatusChecker(EngineStatus status) : IStatusListChecker
    {
        public Task<EngineStatus> CheckAsync(StatusReference statusRef, CancellationToken ct = default) => Task.FromResult(status);
    }

    private static MdocPresentationVerifier BuildVerifier(byte[]? trustedRoot, IStatusListChecker? status = null)
    {
        var registry = new TrustResolverRegistry(new ITrustSourceResolver[]
        {
            new X509TenantTrustSourceResolver(new FakeAnchors(trustedRoot))
        });
        var evaluator = new TrustEvaluator(registry, status);
        var handler = new MdocFormatHandler(new MdocService(), evaluator);
        return new MdocPresentationVerifier(handler, Mock.Of<ILogger<MdocPresentationVerifier>>());
    }

    private static TrustPolicy X509Policy() => new()
    {
        Sources = [new TrustSourceRef { Kind = TrustSourceKind.X509Tenant, ConfersAssurance = AssuranceLevel.Substantial }],
        Combinator = TrustCombinator.AnyOf,
        MinAssuranceLevel = AssuranceLevel.Low
    };

    private static string EncodeVpToken(DeviceResponse response)
        => Base64Url.EncodeToString(MdocCodec.EncodeDeviceResponse(response));

    private sealed record Built(DeviceResponse Response, byte[] IssuerCertDer);

    private static Built BuildMdoc(MsoStatus? status = null)
    {
        var now = DateTimeOffset.UtcNow;
        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=Test PID Issuer", issuerKey, HashAlgorithmName.SHA256);
        using var issuerCert = certReq.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        var issuerCertDer = issuerCert.Export(X509ContentType.Cert);

        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceParams = deviceKey.ExportParameters(false);
        var deviceKeyCose = MdocCbor.Encode(w =>
        {
            w.WriteStartMap(4);
            w.WriteInt32(1); w.WriteInt32(2);
            w.WriteInt32(-1); w.WriteInt32(1);
            w.WriteInt32(-2); w.WriteByteString(deviceParams.Q.X!);
            w.WriteInt32(-3); w.WriteByteString(deviceParams.Q.Y!);
            w.WriteEndMap();
        });

        var elements = new[] { ("family_name", "Andersson"), ("given_name", "Anna") };
        var items = new List<IssuerSignedItemBytes>();
        var digests = new Dictionary<uint, byte[]>();
        uint id = 0;
        foreach (var (name, value) in elements)
        {
            var item = new IssuerSignedItem { DigestId = id, Random = RandomNumberGenerator.GetBytes(16), ElementIdentifier = name, ElementValue = value };
            var tagged = MdocCbor.WrapTag24(MdocCodec.EncodeIssuerSignedItem(item));
            items.Add(new IssuerSignedItemBytes { TaggedBytes = tagged, Item = item });
            digests[id] = SHA256.HashData(tagged);
            id++;
        }

        var mso = new MobileSecurityObject
        {
            ValueDigests = new() { [DocType] = digests },
            DeviceKeyCose = deviceKeyCose,
            DocType = DocType,
            ValidityInfo = new ValidityInfo { Signed = now, ValidFrom = now, ValidUntil = now.AddYears(1) },
            Status = status
        };
        var msoTagged = MdocCbor.WrapTag24(MdocCodec.EncodeMso(mso));
        var unprotected = new CoseHeaderMap { [CoseX5Chain.Label] = CoseX5Chain.Encode([issuerCertDer]) };
        var issuerSigner = new CoseSigner(issuerKey, HashAlgorithmName.SHA256, protectedHeaders: null, unprotectedHeaders: unprotected);
        var issuerAuth = CoseMessage.DecodeSign1(CoseSign1Message.SignEmbedded(msoTagged, issuerSigner));

        var deviceNameSpacesBytes = MdocCbor.WrapTag24(MdocCbor.Encode(w => { w.WriteStartMap(0); w.WriteEndMap(); }));
        var sessionTranscript = MdocCodec.BuildOpenId4VpSessionTranscript(ClientId, Nonce, null, ResponseUri);
        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(sessionTranscript, DocType, deviceNameSpacesBytes);
        var deviceSigner = new CoseSigner(deviceKey, HashAlgorithmName.SHA256);
        var deviceSig = CoseMessage.DecodeSign1(CoseSign1Message.SignDetached(deviceAuthentication, deviceSigner));

        var response = new DeviceResponse
        {
            Documents =
            [
                new Document
                {
                    DocType = DocType,
                    IssuerSigned = new IssuerSigned { NameSpaces = new() { [DocType] = items }, IssuerAuth = issuerAuth },
                    DeviceSigned = new DeviceSigned { NameSpacesBytes = deviceNameSpacesBytes, DeviceAuth = new DeviceAuth { DeviceSignature = deviceSig } }
                }
            ]
        };
        return new Built(response, issuerCertDer);
    }

    [Fact]
    public async Task Verify_ValidMdoc_ChainsToTrustedAnchor_Accepts()
    {
        var built = BuildMdoc();
        var verifier = BuildVerifier(trustedRoot: built.IssuerCertDer);

        var result = await verifier.VerifyAsync(EncodeVpToken(built.Response), ClientId, Nonce, ResponseUri, X509Policy());

        result.IsValid.Should().BeTrue();
        result.VerifiedClaims.Should().ContainKey("family_name");
        result.HolderKeyVerified.Should().BeTrue();
        result.X5cChainValid.Should().BeTrue();
        result.TrustEvidence!.VouchingSource.Should().Be(TrustSourceKind.X509Tenant);
    }

    [Fact]
    public async Task Verify_UntrustedIssuer_FailsClosed()
    {
        var built = BuildMdoc();
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherCert = new CertificateRequest("CN=Other Root", otherKey, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var verifier = BuildVerifier(trustedRoot: otherCert.Export(X509ContentType.Cert));

        var result = await verifier.VerifyAsync(EncodeVpToken(built.Response), ClientId, Nonce, ResponseUri, X509Policy());

        result.IsValid.Should().BeFalse();
        result.X5cChainValid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_TamperedElement_FailsIntegrity()
    {
        var built = BuildMdoc();
        var items = built.Response.Documents[0].IssuerSigned.NameSpaces[DocType].ToList();
        var orig = items[0].Item;
        var tampered = new IssuerSignedItem { DigestId = orig.DigestId, Random = orig.Random, ElementIdentifier = orig.ElementIdentifier, ElementValue = "TAMPERED" };
        items[0] = new IssuerSignedItemBytes { TaggedBytes = MdocCbor.WrapTag24(MdocCodec.EncodeIssuerSignedItem(tampered)), Item = tampered };
        built.Response.Documents[0].IssuerSigned.NameSpaces[DocType] = items;
        var verifier = BuildVerifier(trustedRoot: built.IssuerCertDer);

        var result = await verifier.VerifyAsync(EncodeVpToken(built.Response), ClientId, Nonce, ResponseUri, X509Policy());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("digest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_BadDeviceBinding_FailsHolderBinding()
    {
        var built = BuildMdoc();
        var verifier = BuildVerifier(trustedRoot: built.IssuerCertDer);

        // Wrong nonce → the reconstructed DeviceAuthentication won't match the device signature.
        var result = await verifier.VerifyAsync(EncodeVpToken(built.Response), ClientId, "WRONG-NONCE", ResponseUri, X509Policy());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("device", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_RevokedStatus_FailsRevoked()
    {
        var built = BuildMdoc(status: new MsoStatus { Uri = "https://issuer/status/1", Idx = 4 });
        var verifier = BuildVerifier(trustedRoot: built.IssuerCertDer, status: new FakeStatusChecker(EngineStatus.Invalid));

        var result = await verifier.VerifyAsync(EncodeVpToken(built.Response), ClientId, Nonce, ResponseUri, X509Policy());

        result.IsValid.Should().BeFalse();
        result.StatusCheckResult.Should().Be("Revoked");
    }
}
