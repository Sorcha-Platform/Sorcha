// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="LogoutModel"/> page model.
/// </summary>
public class LogoutModelTests
{
    private readonly Mock<ITokenService> _tokenService = new();

    private LogoutModel CreateModel()
    {
        var model = new LogoutModel(
            _tokenService.Object,
            NullLogger<LogoutModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnPostAsync_WithRefreshToken_RevokesToken()
    {
        // Arrange
        _tokenService
            .Setup(t => t.RevokeTokenAsync("refresh-token-to-revoke", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var model = CreateModel();
        model.RefreshToken = "refresh-token-to-revoke";

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.IsSignedOut.Should().BeTrue();
        _tokenService.Verify(t => t.RevokeTokenAsync("refresh-token-to-revoke", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_WithoutToken_StillShowsSignedOut()
    {
        // Arrange
        var model = CreateModel();
        model.RefreshToken = null;

        // Act
        var result = await model.OnPostAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.IsSignedOut.Should().BeTrue();
        _tokenService.Verify(t => t.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
