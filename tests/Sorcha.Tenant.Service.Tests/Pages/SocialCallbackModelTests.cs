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
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="SocialCallbackModel"/> page model.
/// </summary>
public class SocialCallbackModelTests : IDisposable
{
    private readonly Mock<ISocialLoginService> _socialLoginService = new();
    private readonly Mock<IPlatformUserService> _platformUserService = new();
    private readonly Mock<IIdentityRepository> _identityRepo = new();
    private readonly Mock<IOrganizationRepository> _orgRepo = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<ITransactionalEmailService> _transactionalEmail = new();
    private readonly TenantDbContext _dbContext;

    public SocialCallbackModelTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"SocialCallbackTests-{Guid.NewGuid()}")
            .Options;
        _dbContext = new TenantDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private SocialCallbackModel CreateModel()
    {
        var dispatcher = new WelcomeEmailDispatcher(
            _dbContext, _transactionalEmail.Object, NullLogger<WelcomeEmailDispatcher>.Instance);
        var model = new SocialCallbackModel(
            _socialLoginService.Object,
            _platformUserService.Object,
            _identityRepo.Object,
            _orgRepo.Object,
            _tokenService.Object,
            dispatcher,
            _dbContext,
            NullLogger<SocialCallbackModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Theory]
    [InlineData(null, "state")]
    [InlineData("code", null)]
    public async Task OnGetAsync_MissingParams_ShowsError(
        string? code, string? state)
    {
        // Arrange — provider is recovered from cached state inside ExchangeCodeAsync
        // (feature 115 FR-021), so it is no longer a query parameter.
        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync(code, state, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OnGetAsync_WithError_ShowsErrorMessage()
    {
        // Arrange
        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("code", "state", "access_denied", CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("cancelled or failed");
    }

    [Fact]
    public async Task OnGetAsync_RefusalProviderUnverified_RendersDocumentedCopy()
    {
        // Arrange
        _socialLoginService
            .Setup(s => s.ExchangeCodeAsync(string.Empty, "code", "state", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialAuthCallbackResult(
                Success: true, Error: null,
                Subject: "sub-1", Email: "x@y.com", DisplayName: "X",
                EmailVerified: false, Provider: "Google"));

        _platformUserService
            .Setup(s => s.ResolveOrCreateSocialUserAsync(
                It.IsAny<SocialAuthCallbackResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolveSocialUserResult(
                User: null, IsNew: false, Refusal: SocialLoginRefusal.ProviderUnverified));

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("code", "state", null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("Google");
        model.ErrorMessage.Should().Contain("verify");
    }

    [Fact]
    public async Task OnGetAsync_RefusalExistingUnverified_RendersDocumentedCopy()
    {
        // Arrange
        _socialLoginService
            .Setup(s => s.ExchangeCodeAsync(string.Empty, "code", "state", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SocialAuthCallbackResult(
                Success: true, Error: null,
                Subject: "sub-1", Email: "x@y.com", DisplayName: "X",
                EmailVerified: true, Provider: "Google"));

        _platformUserService
            .Setup(s => s.ResolveOrCreateSocialUserAsync(
                It.IsAny<SocialAuthCallbackResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolveSocialUserResult(
                User: null, IsNew: false, Refusal: SocialLoginRefusal.ExistingUnverified));

        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync("code", "state", null, CancellationToken.None);

        // Assert — copy directs the user to verify their existing account
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("account exists");
        model.ErrorMessage.Should().Contain("verify");
    }
}
