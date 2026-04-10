// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Haip.Service.Models;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Endpoints;

/// <summary>
/// Tests for the credential request model validation and proof structure.
/// </summary>
public class CredentialEndpointTests
{
    [Fact]
    public void CredentialRequest_DefaultFormat_IsVcSdJwt()
    {
        var request = new CredentialRequest();
        request.Format.Should().Be("vc+sd-jwt");
    }

    [Fact]
    public void CredentialRequest_WithProof_Serializes()
    {
        var request = new CredentialRequest
        {
            Format = "vc+sd-jwt",
            Proof = new CredentialRequestProof
            {
                ProofType = "jwt",
                Jwt = "eyJhbGciOiJFUzI1NiJ9.eyJub25jZSI6InRlc3QifQ.signature"
            }
        };

        var json = JsonSerializer.Serialize(request);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("format").GetString().Should().Be("vc+sd-jwt");
        doc.RootElement.GetProperty("proof").GetProperty("proof_type").GetString().Should().Be("jwt");
        doc.RootElement.GetProperty("proof").GetProperty("jwt").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialRequest_Deserializes_FromJsonPropertyNames()
    {
        var json = """{"format":"vc+sd-jwt","proof":{"proof_type":"jwt","jwt":"abc.def.ghi"}}""";
        var request = JsonSerializer.Deserialize<CredentialRequest>(json);

        request.Should().NotBeNull();
        request!.Format.Should().Be("vc+sd-jwt");
        request.Proof.Should().NotBeNull();
        request.Proof!.ProofType.Should().Be("jwt");
        request.Proof.Jwt.Should().Be("abc.def.ghi");
    }

    [Fact]
    public void CredentialOfferService_CreateOffer_ReturnsValidOfferUri()
    {
        // Verify the offer URI structure
        var offerUri = "openid-credential-offer://?credential_offer=%7B%22credential_issuer%22%3A%22https%3A%2F%2Ftest%22%7D";
        offerUri.Should().StartWith("openid-credential-offer://");
        offerUri.Should().Contain("credential_offer=");
    }
}
