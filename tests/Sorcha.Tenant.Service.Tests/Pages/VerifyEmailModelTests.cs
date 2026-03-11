// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="VerifyEmailModel"/> page model.
/// </summary>
public class VerifyEmailModelTests
{
    private readonly Mock<IEmailVerificationService> _emailVerificationService = new();

    private VerifyEmailModel CreateModel()
    {
        var model = new VerifyEmailModel(
            _emailVerificationService.Object,
            NullLogger<VerifyEmailModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnGetAsync_ValidToken_ShowsSuccess()
    {
        // Arrange
        _emailVerificationService
            .Setup(s => s.VerifyTokenAsync("valid-token-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));

        var model = CreateModel();

        // Act
        await model.OnGetAsync("valid-token-abc", CancellationToken.None);

        // Assert
        model.IsVerified.Should().BeTrue();
        model.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnGetAsync_InvalidToken_ShowsError()
    {
        // Arrange
        _emailVerificationService
            .Setup(s => s.VerifyTokenAsync("expired-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "The verification link has expired."));

        var model = CreateModel();

        // Act
        await model.OnGetAsync("expired-token", CancellationToken.None);

        // Assert
        model.IsVerified.Should().BeFalse();
        model.ErrorMessage.Should().Be("The verification link has expired.");
    }

    [Fact]
    public async Task OnGetAsync_MissingToken_ShowsError()
    {
        // Arrange
        var model = CreateModel();

        // Act
        await model.OnGetAsync(null, CancellationToken.None);

        // Assert
        model.IsVerified.Should().BeFalse();
        model.ErrorMessage.Should().Contain("No verification token");
    }
}
