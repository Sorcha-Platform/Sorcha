// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.Cose;

using FluentAssertions;

using Sorcha.Cryptography.Mdoc;
using Sorcha.Cryptography.Mdoc.Cbor;
using Sorcha.Cryptography.Mdoc.Cose;

using Xunit;

namespace Sorcha.Cryptography.Tests.Mdoc;

/// <summary>
/// Feature 135 / T036 — mdoc CBOR/COSE codec round-trips and known-answer checks: tag-24 wrapping
/// preserved verbatim, MSO digests recomputed over the tagged bytes, x5chain on COSE label 33, and
/// the issuer + device COSE_Sign1 signatures verifying after a full encode → decode cycle.
/// </summary>
public class MdocCodecTests
{
    [Fact]
    public void IssuerSignedItem_RoundTrips()
    {
        var item = new IssuerSignedItem
        {
            DigestId = 7,
            Random = RandomNumberGenerator.GetBytes(16),
            ElementIdentifier = "family_name",
            ElementValue = "Andersson"
        };

        var decoded = MdocCodec.DecodeIssuerSignedItem(MdocCodec.EncodeIssuerSignedItem(item));

        decoded.DigestId.Should().Be(7);
        decoded.Random.Should().Equal(item.Random);
        decoded.ElementIdentifier.Should().Be("family_name");
        decoded.ElementValue.Should().Be("Andersson");
    }

    [Fact]
    public void Mso_RoundTrips_IncludingStatusAndValidity()
    {
        var now = DateTimeOffset.UtcNow;
        var mso = new MobileSecurityObject
        {
            DigestAlgorithm = "SHA-256",
            ValueDigests = new() { ["ns"] = new() { [0u] = RandomNumberGenerator.GetBytes(32) } },
            DeviceKeyCose = MdocTestVectors.EncodeEc2CoseKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)),
            DocType = "eu.europa.ec.eudi.pid.1",
            ValidityInfo = new ValidityInfo { Signed = now, ValidFrom = now, ValidUntil = now.AddYears(1) },
            Status = new MsoStatus { Uri = "https://issuer/status/9", Idx = 42 }
        };

        var decoded = MdocCodec.DecodeMso(MdocCodec.EncodeMso(mso));

        decoded.DigestAlgorithm.Should().Be("SHA-256");
        decoded.DocType.Should().Be("eu.europa.ec.eudi.pid.1");
        decoded.ValueDigests["ns"][0u].Should().Equal(mso.ValueDigests["ns"][0u]);
        decoded.Status!.Uri.Should().Be("https://issuer/status/9");
        decoded.Status.Idx.Should().Be(42);
        decoded.ValidityInfo.ValidUntil.Should().BeCloseTo(now.AddYears(1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DeviceResponse_RoundTrips_StructureAndElements()
    {
        var built = MdocTestVectors.BuildPidLike();

        var decoded = MdocCodec.DecodeDeviceResponse(MdocCodec.EncodeDeviceResponse(built.Response));

        decoded.Version.Should().Be("1.0");
        decoded.Status.Should().Be(0u);
        decoded.Documents.Should().ContainSingle();
        var doc = decoded.Documents[0];
        doc.DocType.Should().Be(built.DocType);
        doc.IssuerSigned.NameSpaces.Should().ContainKey(built.DocType);
        var items = doc.IssuerSigned.NameSpaces[built.DocType];
        items.Select(i => i.Item.ElementIdentifier).Should().BeEquivalentTo(built.Elements.Keys);
        items.Single(i => i.Item.ElementIdentifier == "given_name").Item.ElementValue.Should().Be("Anna");
    }

    [Fact]
    public void DeviceResponse_RoundTrip_PreservesTag24DigestInputs()
    {
        var built = MdocTestVectors.BuildPidLike();
        var decoded = MdocCodec.DecodeDeviceResponse(MdocCodec.EncodeDeviceResponse(built.Response));

        var doc = decoded.Documents[0];
        var mso = MdocCodec.DecodeMso(MdocCbor.UnwrapTag24(doc.IssuerSigned.IssuerAuth.Content!.Value));
        var digests = mso.ValueDigests[built.DocType];

        // Each item's recomputed SHA-256 over the verbatim tag-24 bytes must match the MSO digest.
        foreach (var item in doc.IssuerSigned.NameSpaces[built.DocType])
        {
            var recomputed = SHA256.HashData(item.TaggedBytes);
            recomputed.Should().Equal(digests[item.Item.DigestId]);
        }
    }

    [Fact]
    public void DeviceResponse_RoundTrip_IssuerSignatureVerifies_AndX5ChainReadsBack()
    {
        var built = MdocTestVectors.BuildPidLike();
        var decoded = MdocCodec.DecodeDeviceResponse(MdocCodec.EncodeDeviceResponse(built.Response));
        var issuerAuth = decoded.Documents[0].IssuerSigned.IssuerAuth;

        issuerAuth.VerifyEmbedded(built.IssuerKey).Should().BeTrue();

        var chain = CoseX5Chain.Read(issuerAuth);
        chain.Should().NotBeNull();
        chain!.Should().ContainSingle();
        chain[0].Should().Equal(built.IssuerCertDer);
    }

    [Fact]
    public void DeviceResponse_RoundTrip_DeviceSignatureVerifies_OverReconstructedAuthentication()
    {
        var built = MdocTestVectors.BuildPidLike();
        var decoded = MdocCodec.DecodeDeviceResponse(MdocCodec.EncodeDeviceResponse(built.Response));
        var doc = decoded.Documents[0];

        // Reconstruct DeviceAuthentication from the decoded device-namespaces bytes + session transcript.
        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(
            built.SessionTranscript, doc.DocType, doc.DeviceSigned.NameSpacesBytes);

        doc.DeviceSigned.DeviceAuth.DeviceSignature.Should().NotBeNull();
        doc.DeviceSigned.DeviceAuth.DeviceSignature!.VerifyDetached(built.DeviceKey, deviceAuthentication)
            .Should().BeTrue();
    }

    [Fact]
    public void SessionTranscript_IsStable_ForSameInputs()
    {
        var a = MdocCodec.BuildOpenId4VpSessionTranscript("client", "nonce", null, "https://rp/response");
        var b = MdocCodec.BuildOpenId4VpSessionTranscript("client", "nonce", null, "https://rp/response");
        a.Should().Equal(b);

        var different = MdocCodec.BuildOpenId4VpSessionTranscript("client", "OTHER", null, "https://rp/response");
        a.Should().NotEqual(different);
    }
}
