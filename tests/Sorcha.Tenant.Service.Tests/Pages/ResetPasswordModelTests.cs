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
/// Unit tests for <see cref="ResetPasswordModel"/> page model.
/// </summary>
public class ResetPasswordModelTests
{
    private readonly Mock<IPasswordResetService> _passwordResetService = new();

    private ResetPasswordModel CreateModel()
    {
        var model = new ResetPasswordModel(
            _passwordResetService.Object,
            NullLogger<ResetPasswordModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnGet_NoToken_ShowsRequestForm()
    {
        // Arrange
        var model = CreateModel();

        // Act
        await model.OnGetAsync(null, CancellationToken.None);

        // Assert
        model.Mode.Should().Be("request");
        model.Token.Should().BeNull();
        model.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnPostRequestAsync_SendsResetAndShowsConfirmation()
    {
        // Arrange
        _passwordResetService
            .Setup(s => s.RequestResetAsync("user@test.com", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var model = CreateModel();
        model.Email = "user@test.com";

        // Act
        var result = await model.OnPostRequestAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.RequestSent.Should().BeTrue();
        model.Mode.Should().Be("request");
    }

    [Fact]
    public async Task OnGet_ValidToken_ShowsResetForm()
    {
        // Arrange
        _passwordResetService
            .Setup(s => s.ValidateTokenAsync("valid-reset-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResetValidation(true, Email: "user@test.com"));

        var model = CreateModel();

        // Act
        await model.OnGetAsync("valid-reset-token", CancellationToken.None);

        // Assert
        model.Mode.Should().Be("reset");
        model.Token.Should().Be("valid-reset-token");
        model.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnGet_InvalidToken_ShowsError()
    {
        // Arrange
        _passwordResetService
            .Setup(s => s.ValidateTokenAsync("expired-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResetValidation(false, Error: "The reset link has expired."));

        var model = CreateModel();

        // Act
        await model.OnGetAsync("expired-token", CancellationToken.None);

        // Assert
        model.Mode.Should().Be("reset");
        model.ErrorMessage.Should().Contain("expired");
    }

    [Fact]
    public async Task OnPostResetAsync_ValidPassword_ShowsSuccess()
    {
        // Arrange
        _passwordResetService
            .Setup(s => s.ResetPasswordAsync("valid-token", "NewStrongPassword1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResetResult(true));

        var model = CreateModel();
        model.Token = "valid-token";
        model.NewPassword = "NewStrongPassword1!";
        model.ConfirmPassword = "NewStrongPassword1!";

        // Act
        var result = await model.OnPostResetAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ResetSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostResetAsync_WeakPassword_ShowsValidationErrors()
    {
        // Arrange
        var validationErrors = new Dictionary<string, string[]>
        {
            ["NewPassword"] = ["Password must be at least 8 characters.", "Password is too common."]
        };
        _passwordResetService
            .Setup(s => s.ResetPasswordAsync("valid-token", "weak", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResetResult(false, Error: "Password does not meet requirements.", ValidationErrors: validationErrors));

        var model = CreateModel();
        model.Token = "valid-token";
        model.NewPassword = "weak";
        model.ConfirmPassword = "weak";

        // Act
        var result = await model.OnPostResetAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.ResetSuccess.Should().BeFalse();
        model.ErrorMessage.Should().Contain("does not meet requirements");
        model.ValidationErrors.Should().ContainKey("NewPassword");
    }
}
