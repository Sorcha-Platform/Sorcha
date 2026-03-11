// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="SocialCallbackModel"/> page model.
/// </summary>
public class SocialCallbackModelTests
{
    private readonly Mock<ISocialLoginService> _socialLoginService = new();
    private readonly Mock<IPublicUserService> _publicUserService = new();
    private readonly Mock<ITokenService> _tokenService = new();

    private SocialCallbackModel CreateModel()
    {
        var model = new SocialCallbackModel(
            _socialLoginService.Object,
            _publicUserService.Object,
            _tokenService.Object,
            NullLogger<SocialCallbackModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnGetAsync_ValidCode_RedirectsToApp()
    {
        // Arrange
        var identity = new PublicIdentity
        {
            Id = Guid.NewGuid(),
            DisplayName = "Social User",
            Email = "social@test.com"
        };

        _socialLoginService
            .Setup(s => s.ExchangeCodeAsync("Google", "auth-code", "state-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialAuthCallbackResult(
                true, null, "google-sub-123", "social@test.com", "Social User", "Google"));

        _publicUserService
            .Setup(s => s.CreatePublicUserFromSocialAsync(
                "Social User", "social@test.com", It.IsAny<SocialLoginLink>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicUserResult(identity, true));

        _tokenService
            .Setup(t => t.GeneratePublicUserTokenAsync(identity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse { AccessToken = "access-123", RefreshToken = "refresh-456" });

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("Google", "auth-code", "state-123", null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().StartWith("/app/#");
        redirect.Url.Should().Contain("token=");
        redirect.Url.Should().Contain("refresh=");
    }

    [Fact]
    public async Task OnGetAsync_ExchangeFails_ShowsError()
    {
        // Arrange
        _socialLoginService
            .Setup(s => s.ExchangeCodeAsync("Google", "bad-code", "state-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialAuthCallbackResult(false, "Exchange failed", null, null, null, "Google"));

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("Google", "bad-code", "state-123", null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Sign-in failed");
    }

    [Theory]
    [InlineData(null, "code", "state", "missing provider")]
    [InlineData("Google", null, "state", "missing authorization code")]
    [InlineData("Google", "code", null, "missing state")]
    public async Task OnGetAsync_MissingParams_ShowsError(
        string? provider, string? code, string? state, string expectedFragment)
    {
        // Arrange
        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync(provider, code, state, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain(expectedFragment);
    }
}
