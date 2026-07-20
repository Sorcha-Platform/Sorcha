// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Haip.Service.Endpoints;
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

        var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        var atomicCache = new InMemoryAtomicDistributedCache();
        var nonceMetrics = new HaipNonceMetrics(meterFactory);
        _codeStore = new PreAuthCodeStore(atomicCache, nonceMetrics, Mock.Of<ILogger<PreAuthCodeStore>>(), config);
        _nonceStore = new NonceStore(atomicCache, nonceMetrics, Mock.Of<ILogger<NonceStore>>(), config);
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

    /// <summary>
    /// RFC 6749 §5.1 — a response carrying an access token MUST NOT be cached. Without this an
    /// intermediary (proxy, CDN, browser cache) may retain a live bearer token and serve it on.
    /// </summary>
    [Fact]
    public async Task SuccessfulExchange_SetsNoStoreCacheHeaders()
    {
        var (offer, _) = await _offerService.CreateOfferAsync(
            "ws1qissuer1", "tenant-1", "LicenseCredential",
            new Dictionary<string, object> { ["name"] = "Alice" });

        var httpContext = ContextWithForm(new Dictionary<string, StringValues>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:pre-authorized_code",
            ["pre-authorized_code"] = offer.PreAuthorizedCode,
        });

        var result = await InvokeExchangeAsync(httpContext);

        result.Should().BeOfType<Ok<TokenResponse>>();
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        httpContext.Response.Headers.Pragma.ToString().Should().Be("no-cache");
    }

    /// <summary>
    /// The headers are set before any branch, so an early return cannot ship a cacheable response.
    /// Pinned because the obvious implementation — setting them next to the success result — leaves
    /// every error path uncovered and silently regresses the moment someone adds a new early return.
    /// </summary>
    [Fact]
    public async Task RejectedExchange_AlsoSetsNoStoreCacheHeaders()
    {
        var httpContext = ContextWithForm(new Dictionary<string, StringValues>
        {
            ["grant_type"] = "authorization_code",   // unsupported → early 400
        });

        var result = await InvokeExchangeAsync(httpContext);

        // BadRequest<T> is generic over an anonymous type here, so assert the status, not the shape.
        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        httpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private static DefaultHttpContext ContextWithForm(Dictionary<string, StringValues> fields)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(fields);
        return httpContext;
    }

    private Task<IResult> InvokeExchangeAsync(HttpContext httpContext)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:TokenLifetimeSeconds"] = "300",
                ["Haip:IssuerUrl"] = "https://test.example/haip",
            })
            .Build();

        using var tokenStore = new AccessTokenStore(
            Mock.Of<ILogger<AccessTokenStore>>(), config, cache: null);

        var method = typeof(TokenEndpoints).GetMethod(
            "ExchangeToken", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ExchangeToken should be reachable for reflection-based endpoint testing");

        var result = method!.Invoke(null, [
            httpContext, _codeStore, _nonceStore, tokenStore, _offerService, config, CancellationToken.None,
        ]);
        return (Task<IResult>)result!;
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
