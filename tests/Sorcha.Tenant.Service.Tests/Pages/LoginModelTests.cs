// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="LoginModel"/> page model.
/// </summary>
public class LoginModelTests
{
    private readonly Mock<ILoginService> _loginService = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IIdentityRepository> _identityRepo = new();
    private readonly Mock<IOrganizationRepository> _orgRepo = new();

    private LoginModel CreateModel()
    {
        var model = new LoginModel(
            _loginService.Object,
            _totpService.Object,
            _tokenService.Object,
            _identityRepo.Object,
            _orgRepo.Object,
            NullLogger<LoginModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    private static TokenResponse CreateTokens() => new()
    {
        AccessToken = "access-token-123",
        RefreshToken = "refresh-token-456"
    };

    [Fact]
    public async Task OnPostAsync_ValidCredentials_RedirectsToAppWithTokenFragment()
    {
        // Arrange
        var tokens = CreateTokens();
        _loginService.Setup(s => s.LoginAsync("user@test.com", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(true, Tokens: tokens));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "password123";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().StartWith("/app/#");
        redirect.Url.Should().Contain("token=");
        redirect.Url.Should().Contain("refresh=");
    }

    [Fact]
    public async Task OnPostAsync_InvalidCredentials_ShowsError()
    {
        // Arrange
        _loginService.Setup(s => s.LoginAsync("user@test.com", "wrong", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(false, Error: "Invalid email or password."));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "wrong";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task OnPostAsync_TwoFactorRequired_ShowsTwoFactorForm()
    {
        // Arrange
        _loginService.Setup(s => s.LoginAsync("user@test.com", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(
                true,
                TwoFactorRequired: true,
                LoginToken: "login-token-abc",
                AvailableMethods: ["totp"]));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "password123";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ShowTwoFactor.Should().BeTrue();
        model.LoginToken.Should().Be("login-token-abc");
        model.AvailableMethods.Should().Contain("totp");
    }

    [Fact]
    public async Task OnPostAsync_TwoFactorCode_ValidCode_RedirectsToApp()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var user = new UserIdentity
        {
            Id = userId,
            Email = "user@test.com",
            DisplayName = "Test",
            OrganizationId = orgId,
            Status = IdentityStatus.Active
        };
        var org = new Organization
        {
            Id = orgId,
            Name = "TestOrg",
            Subdomain = "test",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tokens = CreateTokens();

        _totpService.Setup(s => s.ValidateLoginTokenAsync("login-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        _totpService.Setup(s => s.ValidateCodeAsync(userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _identityRepo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _orgRepo.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);

        var model = CreateModel();
        model.LoginToken = "login-token";
        model.TotpCode = "123456";
        model.Email = "user@test.com";
        model.Password = "password123";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().StartWith("/app/#");
        redirect.Url.Should().Contain("token=");
    }

    [Fact]
    public async Task OnPostAsync_TwoFactorCode_InvalidCode_ShowsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _totpService.Setup(s => s.ValidateLoginTokenAsync("login-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        _totpService.Setup(s => s.ValidateCodeAsync(userId, "000000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var model = CreateModel();
        model.LoginToken = "login-token";
        model.TotpCode = "000000";
        model.Email = "user@test.com";
        model.Password = "password123";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Be("Invalid verification code.");
        model.ShowTwoFactor.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostAsync_WithReturnUrl_IncludesReturnUrlInFragment()
    {
        // Arrange
        var tokens = CreateTokens();
        _loginService.Setup(s => s.LoginAsync("user@test.com", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(true, Tokens: tokens));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "password123";
        model.ReturnUrl = "/dashboard/settings";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().Contain("returnUrl=");
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com")]
    public async Task OnPostAsync_WithMaliciousReturnUrl_IgnoresReturnUrl(string maliciousUrl)
    {
        // Arrange
        var tokens = CreateTokens();
        _loginService.Setup(s => s.LoginAsync("user@test.com", "password123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(true, Tokens: tokens));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "password123";
        model.ReturnUrl = maliciousUrl;

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().NotContain("returnUrl=");
        redirect.Url.Should().NotContain("evil.com");
    }
}
