// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="OidcCallbackModel"/> page model.
/// </summary>
public class OidcCallbackModelTests : IDisposable
{
    private readonly Mock<IOidcExchangeService> _oidcExchangeService = new();
    private readonly Mock<IOidcProvisioningService> _oidcProvisioningService = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<ITotpService> _totpService = new();
    private readonly Mock<IIdentityRepository> _identityRepo = new();
    private readonly Mock<IOrganizationRepository> _orgRepo = new();
    private readonly TenantDbContext _dbContext;

    public OidcCallbackModelTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName: $"OidcCallbackTests-{Guid.NewGuid()}")
            .Options;
        _dbContext = new TenantDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OidcCallbackModel CreateModel()
    {
        var model = new OidcCallbackModel(
            _oidcExchangeService.Object,
            _oidcProvisioningService.Object,
            _tokenService.Object,
            _totpService.Object,
            _identityRepo.Object,
            _orgRepo.Object,
            _dbContext,
            NullLogger<OidcCallbackModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnGetAsync_ValidCode_RedirectsToApp()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claims = new OidcUserClaims
        {
            Subject = "oidc-sub-123",
            Email = "user@corp.com",
            DisplayName = "Corp User"
        };
        var user = new UserIdentity
        {
            Id = userId,
            Email = "user@corp.com",
            DisplayName = "Corp User",
            OrganizationId = orgId,
            Status = IdentityStatus.Active,
            ProfileCompleted = true
        };
        var org = new Organization
        {
            Id = orgId,
            Name = "Corp",
            Subdomain = "corp",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var tokens = new TokenResponse { AccessToken = "access-123", RefreshToken = "refresh-456" };

        _oidcExchangeService
            .Setup(s => s.ExchangeCodeAsync("auth-code", "state-xyz", "corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OidcExchangeResult { Success = true, Claims = claims, OrgId = orgId });

        _oidcProvisioningService
            .Setup(s => s.CheckDomainRestrictionsAsync(orgId, "user@corp.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _oidcProvisioningService
            .Setup(s => s.ProvisionOrMatchUserAsync(orgId, claims, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, false));
        _oidcProvisioningService
            .Setup(s => s.DetermineProfileCompletionAsync(user))
            .ReturnsAsync(false);

        _orgRepo.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _tokenService.Setup(t => t.GenerateUserTokenAsync(user, org, It.IsAny<CancellationToken>())).ReturnsAsync(tokens);

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("auth-code", "state-xyz", "corp", null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<RedirectResult>();
        var redirect = (RedirectResult)result;
        redirect.Url.Should().StartWith("/app/#");
        redirect.Url.Should().Contain("token=");
    }

    [Fact]
    public async Task OnGetAsync_RequiresProfileCompletion_ShowsForm()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var claims = new OidcUserClaims
        {
            Subject = "oidc-sub-123",
            Email = null,
            DisplayName = null
        };
        var user = new UserIdentity
        {
            Id = Guid.NewGuid(),
            Email = "",
            DisplayName = "",
            OrganizationId = orgId,
            Status = IdentityStatus.Active,
            ProfileCompleted = false
        };

        _oidcExchangeService
            .Setup(s => s.ExchangeCodeAsync("auth-code", "state-xyz", "corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OidcExchangeResult { Success = true, Claims = claims, OrgId = orgId });

        _oidcProvisioningService
            .Setup(s => s.ProvisionOrMatchUserAsync(orgId, claims, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, true));
        _oidcProvisioningService
            .Setup(s => s.DetermineProfileCompletionAsync(user))
            .ReturnsAsync(true);

        _totpService
            .Setup(t => t.GenerateLoginTokenAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("partial-token-abc");

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("auth-code", "state-xyz", "corp", null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.RequiresProfileCompletion.Should().BeTrue();
        model.OidcState.Should().Be("partial-token-abc");
    }

    [Fact]
    public async Task OnGetAsync_ExchangeFails_ShowsError()
    {
        // Arrange
        _oidcExchangeService
            .Setup(s => s.ExchangeCodeAsync("bad-code", "state-xyz", "corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OidcExchangeResult { Success = false, Error = "Token exchange failed" });

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("bad-code", "state-xyz", "corp", null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Sign-in failed");
    }
}
