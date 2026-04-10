// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Sorcha.Haip.Service.Models;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Endpoints;

/// <summary>
/// Tests for the OpenID4VCI issuer metadata and OAuth AS metadata endpoints.
/// </summary>
public class MetadataEndpointTests
{
    private static IConfiguration CreateConfig(string issuerUrl = "https://test.example/haip")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:IssuerUrl"] = issuerUrl
            })
            .Build();
    }

    [Fact]
    public void IssuerMetadata_ContainsRequiredFields()
    {
        // The IssuerMetadata model should have all HAIP 1.0 Section 5 required fields
        var metadata = new IssuerMetadata
        {
            CredentialIssuer = "https://test.example/haip",
            CredentialEndpoint = "https://test.example/haip/credential",
            TokenEndpoint = "https://test.example/haip/token",
            NonceEndpoint = "https://test.example/haip/nonce",
            CredentialsSupported =
            [
                new CredentialSupported
                {
                    Id = "TestCredential",
                    Format = "vc+sd-jwt"
                }
            ]
        };

        metadata.CredentialIssuer.Should().NotBeNullOrWhiteSpace();
        metadata.CredentialEndpoint.Should().NotBeNullOrWhiteSpace();
        metadata.TokenEndpoint.Should().NotBeNullOrWhiteSpace();
        metadata.NonceEndpoint.Should().NotBeNullOrWhiteSpace();
        metadata.CredentialsSupported.Should().NotBeEmpty();
    }

    [Fact]
    public void IssuerMetadata_CredentialsSupportedFormat_IsVcSdJwt()
    {
        var supported = new CredentialSupported
        {
            Id = "TestCredential",
            Format = "vc+sd-jwt",
            CredentialSigningAlgValuesSupported = ["ES256"]
        };

        supported.Format.Should().Be("vc+sd-jwt");
        supported.CryptographicBindingMethodsSupported.Should().Contain("jwk");
        supported.CredentialSigningAlgValuesSupported.Should().Contain("ES256");
    }

    [Fact]
    public void IssuerMetadata_Serializes_WithCorrectJsonPropertyNames()
    {
        var metadata = new IssuerMetadata
        {
            CredentialIssuer = "https://test.example/haip",
            CredentialEndpoint = "https://test.example/haip/credential",
            TokenEndpoint = "https://test.example/haip/token",
            NonceEndpoint = "https://test.example/haip/nonce",
            CredentialsSupported =
            [
                new CredentialSupported { Id = "TestCredential" }
            ]
        };

        var json = JsonSerializer.Serialize(metadata);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("credential_issuer", out _).Should().BeTrue();
        root.TryGetProperty("credential_endpoint", out _).Should().BeTrue();
        root.TryGetProperty("token_endpoint", out _).Should().BeTrue();
        root.TryGetProperty("nonce_endpoint", out _).Should().BeTrue();
        root.TryGetProperty("credentials_supported", out _).Should().BeTrue();
    }

    [Fact]
    public void TokenRequest_Serializes_WithCorrectJsonPropertyNames()
    {
        var request = new TokenRequest
        {
            GrantType = "urn:ietf:params:oauth:grant-type:pre-authorized_code",
            PreAuthorizedCode = "test-code-123"
        };

        var json = JsonSerializer.Serialize(request);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("grant_type", out var gt).Should().BeTrue();
        gt.GetString().Should().Be("urn:ietf:params:oauth:grant-type:pre-authorized_code");
        root.TryGetProperty("pre-authorized_code", out var code).Should().BeTrue();
        code.GetString().Should().Be("test-code-123");
    }

    [Fact]
    public void TokenResponse_Serializes_WithCNonce()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token",
            ExpiresIn = 300,
            CNonce = "test-nonce",
            CNonceExpiresIn = 300
        };

        var json = JsonSerializer.Serialize(response);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("access_token").GetString().Should().Be("test-token");
        root.GetProperty("token_type").GetString().Should().Be("Bearer");
        root.GetProperty("c_nonce").GetString().Should().Be("test-nonce");
        root.GetProperty("c_nonce_expires_in").GetInt32().Should().Be(300);
    }
}
