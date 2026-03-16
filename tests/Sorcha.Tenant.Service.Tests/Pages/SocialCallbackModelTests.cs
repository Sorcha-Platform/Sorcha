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
        var model = new SocialCallbackModel(
            _socialLoginService.Object,
            _platformUserService.Object,
            _identityRepo.Object,
            _orgRepo.Object,
            _tokenService.Object,
            _dbContext,
            NullLogger<SocialCallbackModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Theory]
    [InlineData(null, "code", "state")]
    [InlineData("Google", null, "state")]
    [InlineData("Google", "code", null)]
    public async Task OnGetAsync_MissingParams_ShowsError(
        string? provider, string? code, string? state)
    {
        // Arrange
        var model = CreateModel();

        // Act
        var result = await model.OnGetAsync(provider, code, state, null, CancellationToken.None);

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
        var result = await model.OnGetAsync("Google", "code", "state", "access_denied", CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ErrorMessage.Should().Contain("cancelled or failed");
    }
}
