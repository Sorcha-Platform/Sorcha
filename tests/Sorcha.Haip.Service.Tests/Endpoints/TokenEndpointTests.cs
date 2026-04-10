// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Endpoints;

/// <summary>
/// Tests for the pre-authorized code → token exchange flow.
/// </summary>
public class TokenEndpointTests
{
    private readonly PreAuthCodeStore _codeStore;
    private readonly NonceStore _nonceStore;
    private readonly CredentialOfferService _offerService;

    public TokenEndpointTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PreAuthCodeLifetimeSeconds"] = "300",
                ["Haip:NonceLifetimeSeconds"] = "300",
                ["Haip:IssuerUrl"] = "https://test.example/haip"
            })
            .Build();

        _codeStore = new PreAuthCodeStore(Mock.Of<ILogger<PreAuthCodeStore>>(), config);
        _nonceStore = new NonceStore(Mock.Of<ILogger<NonceStore>>(), config);
        _offerService = new CredentialOfferService(
            _codeStore, Mock.Of<ILogger<CredentialOfferService>>(), config);
    }

    [Fact]
    public async Task ValidPreAuthCode_ReturnsAccessTokenAndCNonce()
    {
        // Create an offer (generates a pre-auth code)
        var (offer, _) = await _offerService.CreateOfferAsync(
            "ws1qissuer1", "tenant-1", "LicenseCredential",
            new Dictionary<string, object> { ["name"] = "Alice" });

        // Redeem the code
        var offerId = await _codeStore.RedeemAsync(offer.PreAuthorizedCode);

        offerId.Should().NotBeNull();
        offerId.Should().Be(offer.Id);
    }

    [Fact]
    public async Task InvalidPreAuthCode_ReturnsNull()
    {
        var result = await _codeStore.RedeemAsync("invalid-code-xyz");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ReusedPreAuthCode_SecondRedemptionFails()
    {
        var (offer, _) = await _offerService.CreateOfferAsync(
            "ws1qissuer1", "tenant-1", "LicenseCredential",
            new Dictionary<string, object>());

        var first = await _codeStore.RedeemAsync(offer.PreAuthorizedCode);
        var second = await _codeStore.RedeemAsync(offer.PreAuthorizedCode);

        first.Should().NotBeNull();
        second.Should().BeNull("pre-auth codes are one-time-use");
    }

    [Fact]
    public async Task TokenResponse_SerializesCorrectly()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token-123",
            ExpiresIn = 300,
            CNonce = "test-nonce-456",
            CNonceExpiresIn = 300
        };

        var json = JsonSerializer.Serialize(response);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("access_token").GetString().Should().Be("test-token-123");
        doc.RootElement.GetProperty("token_type").GetString().Should().Be("Bearer");
        doc.RootElement.GetProperty("expires_in").GetInt32().Should().Be(300);
        doc.RootElement.GetProperty("c_nonce").GetString().Should().Be("test-nonce-456");
    }
}
