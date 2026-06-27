// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Authentication;

public class TokenRefreshServiceTests
{
    private const string ProfileName = "default";

    private readonly Mock<IAuthenticationService> _authService = new();
    private readonly Mock<ITokenCache> _tokenCache = new();
    private readonly Mock<IConfigurationService> _configService = new();
    private readonly CustomAuthenticationStateProvider _authStateProvider;
    private readonly Mock<ILogger<TokenRefreshService>> _logger = new();
    private readonly Mock<ILogger<CustomAuthenticationStateProvider>> _providerLogger = new();
    private readonly Mock<IJSRuntime> _jsRuntime = new();

    public TokenRefreshServiceTests()
    {
        _configService.Setup(x => x.GetActiveProfileNameAsync()).ReturnsAsync(ProfileName);
        _authStateProvider = new CustomAuthenticationStateProvider(
            _tokenCache.Object, _configService.Object, _jsRuntime.Object, _providerLogger.Object);
    }

    private TokenRefreshService CreateService()
    {
        return new TokenRefreshService(
            _authService.Object,
            _tokenCache.Object,
            _configService.Object,
            _authStateProvider,
            _logger.Object);
    }

    private static TokenCacheEntry CreateToken(TimeSpan expiresIn, string? refreshToken = "refresh-token")
    {
        return new TokenCacheEntry
        {
            AccessToken = "access-token",
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.Add(expiresIn),
            ProfileName = ProfileName
        };
    }

    [Fact]
    public async Task ScheduleNextRefreshAsync_NoToken_DoesNotScheduleRefresh()
    {
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync((TokenCacheEntry?)null);

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        _authService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ScheduleNextRefreshAsync_NoRefreshToken_DoesNotScheduleRefresh()
    {
        var entry = CreateToken(TimeSpan.FromMinutes(30), refreshToken: null);
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(entry);

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        _authService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ScheduleNextRefreshAsync_ExpiredToken_RefreshesImmediately()
    {
        var expired = CreateToken(TimeSpan.FromMinutes(-5));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(expired);
        _authService.Setup(x => x.RefreshTokenAsync(ProfileName)).ReturnsAsync(true);

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        _authService.Verify(x => x.RefreshTokenAsync(ProfileName), Times.Once);
    }

    [Fact]
    public async Task ScheduleNextRefreshAsync_NearExpiryToken_RefreshesImmediately()
    {
        // Near expiry = within 5 minutes
        var nearExpiry = CreateToken(TimeSpan.FromMinutes(3));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(nearExpiry);
        _authService.Setup(x => x.RefreshTokenAsync(ProfileName)).ReturnsAsync(true);

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        _authService.Verify(x => x.RefreshTokenAsync(ProfileName), Times.Once);
    }

    [Fact]
    public async Task ScheduleNextRefreshAsync_ValidToken_DoesNotRefreshImmediately()
    {
        var validToken = CreateToken(TimeSpan.FromMinutes(30));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(validToken);

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        // Should schedule timer, not refresh immediately
        _authService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SuccessfulRefresh_NotifiesAuthStateChanged()
    {
        var expired = CreateToken(TimeSpan.FromMinutes(-1));
        var refreshed = CreateToken(TimeSpan.FromMinutes(60));

        // Token stays expired until RefreshTokenAsync succeeds (simulates real behavior).
        // ScheduleNextRefreshAsync reads token (call 1: expired), then RefreshAndRescheduleAsync
        // reads token (call 2: expired), calls refresh, then re-reads (call 3: refreshed).
        var refreshCalled = false;
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName))
            .ReturnsAsync(() => refreshCalled ? refreshed : expired);
        _authService.Setup(x => x.RefreshTokenAsync(ProfileName))
            .ReturnsAsync(() => { refreshCalled = true; return true; });

        // Listen for the AuthenticationStateChanged event on the real provider
        var stateChangedCount = 0;
        _authStateProvider.AuthenticationStateChanged += _ => stateChangedCount++;

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        stateChangedCount.Should().Be(1);
    }

    [Fact]
    public async Task FailedRefresh_DoesNotNotifyAuthState()
    {
        var expired = CreateToken(TimeSpan.FromMinutes(-1));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(expired);
        _authService.Setup(x => x.RefreshTokenAsync(ProfileName)).ReturnsAsync(false);

        var stateChangedCount = 0;
        _authStateProvider.AuthenticationStateChanged += _ => stateChangedCount++;

        using var service = CreateService();
        await service.ScheduleNextRefreshAsync();

        stateChangedCount.Should().Be(0);
    }

    [Fact]
    public async Task OnTabVisible_ExpiredToken_AttemptsRefresh()
    {
        var expired = CreateToken(TimeSpan.FromMinutes(-1));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(expired);
        _authService.Setup(x => x.RefreshTokenAsync(ProfileName)).ReturnsAsync(true);

        using var service = CreateService();
        await service.OnTabVisible();

        _authService.Verify(x => x.RefreshTokenAsync(ProfileName), Times.Once);
    }

    [Fact]
    public async Task OnTabVisible_ValidToken_DoesNotRefresh()
    {
        var valid = CreateToken(TimeSpan.FromMinutes(30));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(valid);

        using var service = CreateService();
        await service.OnTabVisible();

        _authService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DisposedService_OnTabVisible_DoesNothing()
    {
        var expired = CreateToken(TimeSpan.FromMinutes(-1));
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync(expired);

        var service = CreateService();
        service.Dispose();
        await service.OnTabVisible();

        _authService.Verify(x => x.RefreshTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_RegistersJsInterop()
    {
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync((TokenCacheEntry?)null);

        using var service = CreateService();
        await service.StartAsync(_jsRuntime.Object);

        _jsRuntime.Verify(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "TokenLifecycle.register",
            It.IsAny<object?[]>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_UnregistersJsInterop()
    {
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync((TokenCacheEntry?)null);

        using var service = CreateService();
        await service.StartAsync(_jsRuntime.Object);
        await service.StopAsync();

        _jsRuntime.Verify(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "TokenLifecycle.unregister",
            It.IsAny<object?[]>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_JsInteropFails_DoesNotThrow()
    {
        _tokenCache.Setup(x => x.GetTokenAsync(ProfileName)).ReturnsAsync((TokenCacheEntry?)null);
        _jsRuntime.Setup(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            It.IsAny<string>(), It.IsAny<object?[]>()))
            .ThrowsAsync(new JSException("JS not available"));

        using var service = CreateService();
        var act = () => service.StartAsync(_jsRuntime.Object);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var service = CreateService();
        service.Dispose();

        var act = () => service.Dispose();
        act.Should().NotThrow();
    }
}
