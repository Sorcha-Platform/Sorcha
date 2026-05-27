// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class KidThumbprintHelperTests
{
    // RFC 7638 §3.1 worked example (RSA key) — thumbprint:
    // NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs
    private const string Rfc7638RsaJwk = """
        {
          "kty":"RSA",
          "n":"0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw",
          "e":"AQAB",
          "alg":"RS256",
          "kid":"2011-04-29"
        }
        """;

    [Fact]
    public void TryComputeThumbprint_Rfc7638RsaExample_ReturnsExpectedThumbprint()
    {
        using var doc = JsonDocument.Parse(Rfc7638RsaJwk);
        var success = KidThumbprintHelper.TryComputeThumbprint(doc.RootElement, out var thumbprint);

        success.Should().BeTrue();
        thumbprint.Should().Be("NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs");
    }

    [Fact]
    public void TryMatchExact_KidMatchesVm_ReturnsVm()
    {
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = "did:sorcha:org:abc",
                    PublicKeyMultibase = "zPlaceholder"
                }
            ]
        };

        var found = KidThumbprintHelper.TryMatchExact(doc, "did:sorcha:org:abc#vc-issuance-1", out var vm);

        found.Should().BeTrue();
        vm!.Id.Should().Be("did:sorcha:org:abc#vc-issuance-1");
    }

    [Fact]
    public void TryMatchExact_KidDoesNotMatch_ReturnsFalse()
    {
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = "did:sorcha:org:abc",
                    PublicKeyMultibase = "zPlaceholder"
                }
            ]
        };

        var found = KidThumbprintHelper.TryMatchExact(doc, "did:sorcha:org:abc#vc-issuance-2", out var vm);

        found.Should().BeFalse();
        vm.Should().BeNull();
    }

    [Fact]
    public void TryMatchExact_EmptyKid_ReturnsFalse()
    {
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod = [
                new VerificationMethod { Id = "x", Type = "y", Controller = "z" }
            ]
        };

        KidThumbprintHelper.TryMatchExact(doc, "", out _).Should().BeFalse();
    }

    [Fact]
    public void TryMatchByThumbprint_KidIsFullDidUrl_MatchesByFragment()
    {
        using var jwkDoc = JsonDocument.Parse(Rfc7638RsaJwk);
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs",
                    Type = "JsonWebKey2020",
                    Controller = "did:sorcha:org:abc",
                    PublicKeyJwk = jwkDoc.RootElement.Clone()
                }
            ]
        };

        var found = KidThumbprintHelper.TryMatchByThumbprint(
            doc,
            "did:sorcha:org:abc#NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs",
            out var vm);

        found.Should().BeTrue();
        vm!.Id.Should().EndWith("NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs");
    }

    [Fact]
    public void TryMatchByThumbprint_KidIsBareThumbprint_Matches()
    {
        using var jwkDoc = JsonDocument.Parse(Rfc7638RsaJwk);
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = "did:sorcha:org:abc",
                    PublicKeyJwk = jwkDoc.RootElement.Clone()
                }
            ]
        };

        var found = KidThumbprintHelper.TryMatchByThumbprint(
            doc,
            "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs",
            out var vm);

        found.Should().BeTrue();
        vm!.Id.Should().Be("did:sorcha:org:abc#vc-issuance-1");
    }

    [Fact]
    public void TryMatchByThumbprint_NoMatch_ReturnsFalse()
    {
        using var jwkDoc = JsonDocument.Parse(Rfc7638RsaJwk);
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = "did:sorcha:org:abc",
                    PublicKeyJwk = jwkDoc.RootElement.Clone()
                }
            ]
        };

        var found = KidThumbprintHelper.TryMatchByThumbprint(doc, "did:sorcha:org:abc#wrong-thumb", out var vm);

        found.Should().BeFalse();
        vm.Should().BeNull();
    }

    [Fact]
    public void TryMatchByThumbprint_VmHasNoJwk_SkipsAndReturnsFalse()
    {
        var doc = new DidDocument
        {
            Id = "did:sorcha:org:abc",
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = "did:sorcha:org:abc#vc-issuance-1",
                    Type = "JsonWebKey2020",
                    Controller = "did:sorcha:org:abc",
                    PublicKeyMultibase = "zPlaceholder"   // multibase-only — Phase 1 limitation
                }
            ]
        };

        var found = KidThumbprintHelper.TryMatchByThumbprint(
            doc,
            "did:sorcha:org:abc#anything",
            out var vm);

        found.Should().BeFalse();
        vm.Should().BeNull();
    }
}
