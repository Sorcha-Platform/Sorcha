// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private readonly Mock<ISocialLoginService> _socialLoginService = new();
    private readonly Mock<IPlatformUserDeviceService> _deviceService = new();

    private LoginModel CreateModel()
    {
        var model = new LoginModel(
            _loginService.Object,
            _totpService.Object,
            _tokenService.Object,
            _identityRepo.Object,
            _orgRepo.Object,
            NullLogger<LoginModel>.Instance,
            Options.Create(new DemoEnvironmentSettings()),
            _socialLoginService.Object,
            Options.Create(new ReturnToAllowlistOptions()),
            _deviceService.Object);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public void OnGet_PopulatesAvailableProvidersFromSocialLoginService()
    {
        // Arrange
        _socialLoginService
            .Setup(s => s.GetConfiguredProviderNames())
            .Returns(new[] { "Google", "GitHub" });

        var model = CreateModel();

        // Act
        model.OnGet();

        // Assert — login page shows the same set of providers as the signup page.
        model.AvailableProviders.Should().Equal("Google", "GitHub");
    }

    [Fact]
    public void OnGet_NoConfiguredProviders_AvailableProvidersEmpty()
    {
        // Arrange
        _socialLoginService
            .Setup(s => s.GetConfiguredProviderNames())
            .Returns(Array.Empty<string>());

        var model = CreateModel();

        // Act
        model.OnGet();

        // Assert
        model.AvailableProviders.Should().BeEmpty();
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
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task OnPostAsync_OrgSelection_RedirectsToAppWithSelectedOrg()
    {
        // Arrange — user selects Org B from org picker (no 2FA)
        var orgBId = Guid.NewGuid();
        var tokens = CreateTokens();

        _loginService.Setup(s => s.CompleteOrgSelectionAsync(
                "platform-token", orgBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(true, Tokens: tokens));

        var model = CreateModel();
        model.PlatformLoginToken = "platform-token";
        model.SelectedOrgId = orgBId;

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().StartWith("/app/#");
        redirect.Url.Should().Contain("token=");
    }

    [Fact]
    public async Task OnPostAsync_OrgSelectionWith2FA_PreservesSelectedOrgId()
    {
        // Arrange — user selects Org B, but 2FA is required
        var orgBId = Guid.NewGuid();

        _loginService.Setup(s => s.CompleteOrgSelectionAsync(
                "platform-token", orgBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(
                true,
                TwoFactorRequired: true,
                LoginToken: "2fa-login-token",
                AvailableMethods: ["totp"]));

        var model = CreateModel();
        model.PlatformLoginToken = "platform-token";
        model.SelectedOrgId = orgBId;

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert — 2FA form shown with SelectedOrgId preserved
        result.Should().BeOfType<PageResult>();
        model.ShowTwoFactor.Should().BeTrue();
        model.LoginToken.Should().Be("2fa-login-token");
        model.SelectedOrgId.Should().Be(orgBId);
    }

    [Fact]
    public async Task Handle2FaAsync_WithSelectedOrgId_UsesSelectedOrgNotDefault()
    {
        // Arrange — user belongs to Org A (default) and Org B (selected)
        var userId = Guid.NewGuid();
        var orgAId = Guid.NewGuid();
        var orgBId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        var userInOrgA = new UserIdentity
        {
            Id = userId,
            Email = "admin@test.com",
            DisplayName = "Admin",
            OrganizationId = orgAId,
            PlatformUserId = platformUserId,
            Status = IdentityStatus.Active
        };
        var userInOrgB = new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = "admin@test.com",
            DisplayName = "Admin",
            OrganizationId = orgBId,
            PlatformUserId = platformUserId,
            Status = IdentityStatus.Active
        };
        var orgB = new Organization
        {
            Id = orgBId,
            Name = "Org B",
            Subdomain = "org-b",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tokens = CreateTokens();

        _totpService.Setup(s => s.ValidateLoginTokenAsync("login-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        _totpService.Setup(s => s.ValidateCodeAsync(userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _identityRepo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userInOrgA);
        _identityRepo.Setup(r => r.GetUserByPlatformUserAndOrgAsync(
                platformUserId, orgBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userInOrgB);
        _orgRepo.Setup(r => r.GetByIdAsync(orgBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(orgB);
        _tokenService.Setup(t => t.GenerateUserTokenAsync(
                userInOrgB, orgB, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);

        var model = CreateModel();
        model.LoginToken = "login-token";
        model.TotpCode = "123456";
        model.SelectedOrgId = orgBId; // This is the security-critical field

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert — token generated for Org B, NOT Org A
        result.Should().BeOfType<RedirectResult>();
        _tokenService.Verify(t => t.GenerateUserTokenAsync(
            userInOrgB, orgB, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once, "JWT must be generated for the selected org (Org B), not the user's default org");
        _orgRepo.Verify(r => r.GetByIdAsync(orgAId, It.IsAny<CancellationToken>()),
            Times.Never, "Should NOT look up Org A — user selected Org B");
    }

    [Fact]
    public async Task Handle2FaAsync_WithTamperedOrgId_RejectsNonMember()
    {
        // Arrange — user submits 2FA with a SelectedOrgId they don't belong to
        var userId = Guid.NewGuid();
        var orgAId = Guid.NewGuid();
        var tamperedOrgId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        var userInOrgA = new UserIdentity
        {
            Id = userId,
            Email = "admin@test.com",
            DisplayName = "Admin",
            OrganizationId = orgAId,
            PlatformUserId = platformUserId,
            Status = IdentityStatus.Active
        };
        var tamperedOrg = new Organization
        {
            Id = tamperedOrgId,
            Name = "Victim Org",
            Subdomain = "victim",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _totpService.Setup(s => s.ValidateLoginTokenAsync("login-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);
        _totpService.Setup(s => s.ValidateCodeAsync(userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _identityRepo.Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userInOrgA);
        _orgRepo.Setup(r => r.GetByIdAsync(tamperedOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tamperedOrg);
        // User is NOT a member of the tampered org
        _identityRepo.Setup(r => r.GetUserByPlatformUserAndOrgAsync(
                platformUserId, tamperedOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity?)null);

        var model = CreateModel();
        model.LoginToken = "login-token";
        model.TotpCode = "123456";
        model.SelectedOrgId = tamperedOrgId;

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert — rejects with error, does NOT issue a token
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Be("You are not a member of the selected organization.");
        _tokenService.Verify(t => t.GenerateUserTokenAsync(
            It.IsAny<UserIdentity>(), It.IsAny<Organization>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "Must NOT issue a JWT for an org the user doesn't belong to");
    }

    [Fact]
    public async Task Handle2FaAsync_WithoutSelectedOrgId_UsesUserDefaultOrg()
    {
        // Arrange — single-org user, no SelectedOrgId set (normal 2FA flow)
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
            Name = "Default Org",
            Subdomain = "default",
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
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);

        var model = CreateModel();
        model.LoginToken = "login-token";
        model.TotpCode = "123456";
        // SelectedOrgId NOT set — single-org login

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert — falls back to user's default org
        result.Should().BeOfType<RedirectResult>();
        _tokenService.Verify(t => t.GenerateUserTokenAsync(
            user, org, It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
