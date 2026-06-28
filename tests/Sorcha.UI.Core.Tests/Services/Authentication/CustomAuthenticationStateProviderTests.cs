// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Authentication;

public class CustomAuthenticationStateProviderTests
{
    private const string ProfileName = "default";

    private readonly Mock<ITokenCache> _tokenCache = new();
    private readonly Mock<IConfigurationService> _configService = new();
    private readonly Mock<IJSRuntime> _jsRuntime = new();
    private readonly Mock<ILogger<CustomAuthenticationStateProvider>> _logger = new();
    private readonly CustomAuthenticationStateProvider _provider;

    public CustomAuthenticationStateProviderTests()
    {
        _configService.Setup(x => x.GetActiveProfileNameAsync()).ReturnsAsync(ProfileName);
        _provider = new CustomAuthenticationStateProvider(
            _tokenCache.Object, _configService.Object, _jsRuntime.Object, _logger.Object);
    }

    private static string CreateTestJwt(TimeSpan validFor, string sub = "test-user")
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(new byte[32]);
        var token = new JwtSecurityToken(
            claims: [new Claim("sub", sub)],
            expires: DateTime.UtcNow.Add(validFor),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return handler.WriteToken(token);
    }

    private void SetupNoFragmentToken()
    {
        _jsRuntime.Setup(x => x.InvokeAsync<string?>(
            "localStorage.getItem", It.IsAny<object?[]?>()))
            .ReturnsAsync((string?)null);
        _jsRuntime.Setup(x => x.InvokeAsync<string?>(
            "sorcha.fragmentHandoff.getWindowToken", It.IsAny<object?[]?>()))
            .ReturnsAsync((string?)null);
    }

    private void SetupFragmentToken(string jwt)
    {
        var json = JsonSerializer.Serialize(new { token = jwt, refresh = "refresh-token" });
        _jsRuntime.Setup(x => x.InvokeAsync<string?>(
            "localStorage.getItem", It.IsAny<object?[]?>()))
            .ReturnsAsync(json);
        _jsRuntime.Setup(x => x.InvokeAsync<IJSVoidResult>(
            "sorcha.fragmentHandoff.clearTokenStaging", It.IsAny<object?[]?>()))
            .ReturnsAsync(Mock.Of<IJSVoidResult>());
        _tokenCache.Setup(x => x.StoreTokenAsync(It.IsAny<string>(), It.IsAny<TokenCacheEntry>()))
            .Returns(Task.CompletedTask);
    }

    // C1 — Fresh consume raises exactly one change event
    [Fact]
    public async Task FreshStagedToken_Consumed_RaisesAuthStateChangedOnce()
    {
        var jwt = CreateTestJwt(TimeSpan.FromHours(1));
        SetupFragmentToken(jwt);

        var eventCount = 0;
        _provider.AuthenticationStateChanged += _ => eventCount++;

        var state = await _provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        eventCount.Should().Be(1);
    }

    // C2 — No event on cache-only resolution
    [Fact]
    public async Task CacheOnly_NoFragmentToken_NoConsumePathEvent()
    {
        var jwt = CreateTestJwt(TimeSpan.FromHours(1));
        SetupNoFragmentToken();
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName))
            .ReturnsAsync(new TokenCacheEntry
            {
                AccessToken = jwt,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                ProfileName = ProfileName
            });

        var eventCount = 0;
        _provider.AuthenticationStateChanged += _ => eventCount++;

        var state = await _provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        eventCount.Should().Be(0);
    }

    // C3 — No event for absent/expired/invalid token
    [Fact]
    public async Task ExpiredOrAbsentToken_NoSignedInEvent_StateAnonymous()
    {
        // No staged token and no cached token
        SetupNoFragmentToken();
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName))
            .ReturnsAsync((TokenCacheEntry?)null);

        var eventCount = 0;
        _provider.AuthenticationStateChanged += _ => eventCount++;

        var state = await _provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse();
        eventCount.Should().Be(0);
    }

    // C4 — Idempotent, single-consume
    [Fact]
    public async Task SecondCallAfterConsume_NoDoubleStore_NoExtraEvent()
    {
        var jwt = CreateTestJwt(TimeSpan.FromHours(1));
        SetupFragmentToken(jwt);
        // Cache returns null after first consume (staging cleared, no cache entry before first consume)
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName))
            .ReturnsAsync((TokenCacheEntry?)null);

        var eventCount = 0;
        _provider.AuthenticationStateChanged += _ => eventCount++;

        // First call — consumes staged token, raises one event
        await _provider.GetAuthenticationStateAsync();

        // Second call — returns in-flight or cached Task2 (re-query after broadcast)
        await _provider.GetAuthenticationStateAsync();

        _tokenCache.Verify(x => x.StoreTokenAsync(ProfileName, It.IsAny<TokenCacheEntry>()), Times.Once);
        eventCount.Should().Be(1);
    }

    // C5 — Existing notify callers unchanged
    [Fact]
    public async Task DirectNotifyCall_ResetsAndRebroadcasts()
    {
        var jwt = CreateTestJwt(TimeSpan.FromHours(1));
        SetupNoFragmentToken();
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName))
            .ReturnsAsync(new TokenCacheEntry
            {
                AccessToken = jwt,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                ProfileName = ProfileName
            });

        var eventCount = 0;
        _provider.AuthenticationStateChanged += _ => eventCount++;

        // Simulate TokenRefreshService / OrgSwitcher / LogoutConfirmDialog / MainLayout calling notify
        _provider.NotifyAuthenticationStateChanged();

        // Event raised synchronously
        eventCount.Should().Be(1);

        // Re-evaluation happens — next GetAuthenticationStateAsync uses re-queried state
        var state = await _provider.GetAuthenticationStateAsync();
        state.User.Identity!.IsAuthenticated.Should().BeTrue();
    }
}
