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
/// Unit tests for <see cref="SignupModel"/> page model.
/// </summary>
public class SignupModelTests
{
    private readonly Mock<IRegistrationService> _registrationService = new();

    private SignupModel CreateModel()
    {
        var model = new SignupModel(
            _registrationService.Object,
            NullLogger<SignupModel>.Instance);

        var httpContext = new DefaultHttpContext();
        model.PageContext = new PageContext(new ActionContext(
            httpContext, new RouteData(), new PageActionDescriptor()));

        return model;
    }

    [Fact]
    public async Task OnPostEmailAsync_ValidRegistration_ShowsSuccess()
    {
        // Arrange
        _registrationService
            .Setup(s => s.RegisterAsync("default", "user@test.com", "StrongPassword1!", "Test User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(true, UserId: Guid.NewGuid(), Message: "Verification email sent"));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "StrongPassword1!";
        model.ConfirmPassword = "StrongPassword1!";
        model.DisplayName = "Test User";

        // Act
        var result = await model.OnPostEmailAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.RegistrationSuccess.Should().BeTrue();
        model.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task OnPostEmailAsync_ValidationErrors_ShowsErrors()
    {
        // Arrange
        var validationErrors = new Dictionary<string, string[]>
        {
            ["Password"] = ["Password must be at least 8 characters."]
        };
        _registrationService
            .Setup(s => s.RegisterAsync("default", "user@test.com", "weak", "Test User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(false, ValidationErrors: validationErrors, Error: "Validation failed."));

        var model = CreateModel();
        model.Email = "user@test.com";
        model.Password = "weak";
        model.ConfirmPassword = "weak";
        model.DisplayName = "Test User";

        // Act
        var result = await model.OnPostEmailAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.RegistrationSuccess.Should().BeFalse();
        model.ErrorMessage.Should().Be("Validation failed.");
        model.ValidationErrors.Should().ContainKey("Password");
    }

    [Fact]
    public async Task OnPostEmailAsync_DuplicateEmail_ShowsError()
    {
        // Arrange
        _registrationService
            .Setup(s => s.RegisterAsync("default", "existing@test.com", "StrongPassword1!", "Test User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(false, Error: "A user with this email already exists.", ErrorStatusCode: 409));

        var model = CreateModel();
        model.Email = "existing@test.com";
        model.Password = "StrongPassword1!";
        model.ConfirmPassword = "StrongPassword1!";
        model.DisplayName = "Test User";

        // Act
        var result = await model.OnPostEmailAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.RegistrationSuccess.Should().BeFalse();
        model.ErrorMessage.Should().Contain("already exists");
    }

    [Fact]
    public async Task OnPostEmailAsync_RegistrationWithEmailVerification_ShowsSuccessMessage()
    {
        // Arrange — registration succeeds but no tokens are returned (email verification required)
        _registrationService
            .Setup(s => s.RegisterAsync("default", "new@test.com", "StrongPassword1!", "New User", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(true, UserId: Guid.NewGuid(), Message: "Verification email sent"));

        var model = CreateModel();
        model.Email = "new@test.com";
        model.Password = "StrongPassword1!";
        model.ConfirmPassword = "StrongPassword1!";
        model.DisplayName = "New User";

        // Act
        var result = await model.OnPostEmailAsync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<PageResult>();
        model.RegistrationSuccess.Should().BeTrue();
    }
}
