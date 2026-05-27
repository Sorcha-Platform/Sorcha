// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using Sorcha.Cryptography.Mdoc;
using Sorcha.Cryptography.Mdoc.Cbor;

using Xunit;

namespace Sorcha.Cryptography.Tests.Mdoc;

/// <summary>
/// Feature 135 / T037 — MdocService verification: issuer COSE_Sign1 over the MSO, value-digest
/// integrity over the tag-24 items, and device-auth holder binding against the reconstructed
/// OpenID4VP DeviceAuthentication / SessionTranscript. Uses a generated PID-shape vector.
/// </summary>
public class MdocServiceTests
{
    private readonly MdocService _service = new();

    private static MdocSessionTranscript Transcript(MdocTestVectors.BuiltMdoc built) =>
        new() { ClientId = built.ClientId, Nonce = built.Nonce, ResponseUri = built.ResponseUri };

    [Fact]
    public void Verify_ValidMdoc_AllChecksPass_AndSurfacesClaims()
    {
        var built = MdocTestVectors.BuildPidLike();
        var bytes = MdocCodec.EncodeDeviceResponse(built.Response);

        var result = _service.Verify(bytes, Transcript(built));

        result.IsValid.Should().BeTrue();
        result.IssuerSignatureValid.Should().BeTrue();
        result.DigestsValid.Should().BeTrue();
        result.DeviceBindingValid.Should().BeTrue();
        result.DocType.Should().Be(built.DocType);
        result.Claims.Should().ContainKey("family_name").WhoseValue.Should().Be("Andersson");
        result.Claims.Should().ContainKey("given_name").WhoseValue.Should().Be("Anna");
        result.X5cChain.Should().NotBeNull();
        result.X5cChain!.Should().ContainSingle();
        result.Status.Should().BeNull();
    }

    [Fact]
    public void Verify_WithStatus_SurfacesStatusReference()
    {
        var built = MdocTestVectors.BuildPidLike(status: new MsoStatus { Uri = "https://issuer/status/3", Idx = 17 });
        var bytes = MdocCodec.EncodeDeviceResponse(built.Response);

        var result = _service.Verify(bytes, Transcript(built));

        result.IsValid.Should().BeTrue();
        result.Status.Should().NotBeNull();
        result.Status!.Uri.Should().Be("https://issuer/status/3");
        result.Status.Idx.Should().Be(17u);
    }

    [Fact]
    public void Verify_TamperedElement_DigestsInvalid()
    {
        var built = MdocTestVectors.BuildPidLike();
        var doc = built.Response.Documents[0];
        var ns = built.DocType;
        var items = doc.IssuerSigned.NameSpaces[ns].ToList();
        var original = items[0].Item;
        var tampered = new IssuerSignedItem
        {
            DigestId = original.DigestId,
            Random = original.Random,
            ElementIdentifier = original.ElementIdentifier,
            ElementValue = "TAMPERED"
        };
        items[0] = new IssuerSignedItemBytes
        {
            TaggedBytes = MdocCbor.WrapTag24(MdocCodec.EncodeIssuerSignedItem(tampered)),
            Item = tampered
        };
        doc.IssuerSigned.NameSpaces[ns] = items;
        var bytes = MdocCodec.EncodeDeviceResponse(built.Response);

        var result = _service.Verify(bytes, Transcript(built));

        result.IsValid.Should().BeFalse();
        result.DigestsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongSessionTranscript_DeviceBindingInvalid()
    {
        var built = MdocTestVectors.BuildPidLike();
        var bytes = MdocCodec.EncodeDeviceResponse(built.Response);

        var wrong = new MdocSessionTranscript { ClientId = built.ClientId, Nonce = "WRONG-NONCE", ResponseUri = built.ResponseUri };
        var result = _service.Verify(bytes, wrong);

        result.IsValid.Should().BeFalse();
        result.DeviceBindingValid.Should().BeFalse();
        // Issuer signature + digests are unaffected by the holder-binding mismatch.
        result.IssuerSignatureValid.Should().BeTrue();
        result.DigestsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_IssuerKeyNotMatchingX5cLeaf_IssuerSignatureInvalid()
    {
        // The MSO is signed by the real issuer key, but the x5chain leaf is a different cert —
        // the leaf public key cannot verify the issuer signature.
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherReq = new CertificateRequest("CN=Imposter", otherKey, HashAlgorithmName.SHA256);
        using var otherCert = otherReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var built = MdocTestVectors.BuildPidLike(issuerChain: [otherCert.Export(X509ContentType.Cert)]);
        var bytes = MdocCodec.EncodeDeviceResponse(built.Response);

        var result = _service.Verify(bytes, Transcript(built));

        result.IsValid.Should().BeFalse();
        result.IssuerSignatureValid.Should().BeFalse();
    }
}
