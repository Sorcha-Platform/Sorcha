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
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="SignupModel"/> page model.
/// </summary>
public class SignupModelTests
{
    private readonly Mock<IRegistrationService> _registrationService = new();
    private readonly Mock<ISocialLoginService> _socialLoginService = new();

    private SignupModel CreateModel()
    {
        var model = new SignupModel(
            _registrationService.Object,
            NullLogger<SignupModel>.Instance,
            Options.Create(new DemoEnvironmentSettings()),
            _socialLoginService.Object,
            Options.Create(new ReturnToAllowlistOptions()));

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

        // Assert
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

    [Fact]
    public void OnGet_PreservesProviderNameCasingFromService()
    {
        // Arrange — provider names should be returned exactly as the service returns them.
        _socialLoginService
            .Setup(s => s.GetConfiguredProviderNames())
            .Returns(new[] { "GitHub" });

        var model = CreateModel();

        // Act
        model.OnGet();

        // Assert
        model.AvailableProviders.Should().ContainSingle().Which.Should().Be("GitHub");
    }

    [Fact]
    public async Task OnPostEmailAsync_ValidRegistration_ShowsSuccess()
    {
        // Arrange
        _registrationService
            .Setup(s => s.RegisterAsync("public", "user@test.com", "StrongPassword1!", "Test User", It.IsAny<CancellationToken>()))
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
            .Setup(s => s.RegisterAsync("public", "user@test.com", "weak", "Test User", It.IsAny<CancellationToken>()))
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
            .Setup(s => s.RegisterAsync("public", "existing@test.com", "StrongPassword1!", "Test User", It.IsAny<CancellationToken>()))
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
            .Setup(s => s.RegisterAsync("public", "new@test.com", "StrongPassword1!", "New User", It.IsAny<CancellationToken>()))
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
