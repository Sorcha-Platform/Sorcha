// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.Register.Models.Enums;
using Sorcha.UI.Core.Components.Registers;
using Sorcha.UI.Core.Models.Registers;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Registers;

/// <summary>
/// Tests for the RegisterCard component.
/// </summary>
public class RegisterCardTests : BunitContext
{
    public RegisterCardTests()
    {
        // Add MudBlazor services for component rendering
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static RegisterViewModel CreateTestRegister(
        string name = "Test Register",
        RegisterStatus status = RegisterStatus.Online,
        uint height = 1234,
        bool advertise = false)
    {
        return new RegisterViewModel
        {
            Id = "12345678901234567890123456789012",
            Name = name,
            Status = status,
            Height = height,
            Advertise = advertise,
            IsFullReplica = true,
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
    }

    [Fact]
    public void RegisterCard_DisplaysRegisterName()
    {
        // Arrange
        var register = CreateTestRegister(name: "My Test Register");

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert
        cut.Markup.Should().Contain("My Test Register");
    }

    [Fact]
    public void RegisterCard_DisplaysFullId()
    {
        // Arrange
        var register = CreateTestRegister();

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert — full ID is now displayed (not truncated)
        cut.Markup.Should().Contain("12345678901234567890123456789012");
    }

    [Fact]
    public void RegisterCard_DisplaysDescription_WhenPresent()
    {
        // Arrange
        var register = CreateTestRegister() with { Description = "A test description" };

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert
        cut.Markup.Should().Contain("A test description");
    }

    [Fact]
    public void RegisterCard_HidesDescription_WhenNull()
    {
        // Arrange
        var register = CreateTestRegister();

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert — no description element rendered
        cut.FindAll("[data-testid='register-description']").Should().BeEmpty();
    }

    [Fact]
    public void RegisterCard_ShowsPrivate_WhenAdvertiseIsFalse()
    {
        // Arrange
        var register = CreateTestRegister(advertise: false);

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert
        cut.Markup.Should().Contain("Private");
    }

    [Fact]
    public void RegisterCard_ShowsPublicChip_WhenAdvertiseIsTrue()
    {
        // Arrange
        var register = CreateTestRegister(advertise: true);

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert
        cut.Markup.Should().Contain("Public");
    }

    [Fact]
    public void RegisterCard_ShowsPrivateNotPublic_WhenAdvertiseIsFalse()
    {
        // Arrange
        var register = CreateTestRegister(advertise: false);

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert - "Private" should appear, "Public" should not
        cut.Markup.Should().Contain("Private");
        cut.Markup.Should().NotContain(">Public<");
    }

    [Fact]
    public void RegisterCard_ContainsStatusBadge()
    {
        // Arrange
        var register = CreateTestRegister(status: RegisterStatus.Online);

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert - The RegisterStatusBadge component should be rendered
        var badgeComponent = cut.FindComponent<RegisterStatusBadge>();
        badgeComponent.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterCard_InvokesOnClick_WhenCardClicked()
    {
        // Arrange
        var register = CreateTestRegister();
        RegisterViewModel? clickedRegister = null;

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register)
            .Add(p => p.OnClick, EventCallback.Factory.Create<RegisterViewModel>(
                this, (r) => clickedRegister = r)));

        var card = cut.Find("[data-testid='register-card']");
        await card.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        clickedRegister.Should().NotBeNull();
        clickedRegister!.Id.Should().Be(register.Id);
    }

    [Fact]
    public void RegisterCard_HasCorrectTestId()
    {
        // Arrange
        var register = CreateTestRegister();

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert
        var card = cut.Find("[data-testid='register-card']");
        card.Should().NotBeNull();
    }

    [Theory]
    [InlineData(RegisterStatus.Online)]
    [InlineData(RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Checking)]
    [InlineData(RegisterStatus.Recovery)]
    public void RegisterCard_RendersForAllStatuses(RegisterStatus status)
    {
        // Arrange
        var register = CreateTestRegister(status: status);

        // Act
        var cut = Render<RegisterCard>(parameters => parameters
            .Add(p => p.Register, register));

        // Assert - Should render without errors
        cut.Markup.Should().NotBeNullOrEmpty();
        cut.Markup.Should().Contain("Test Register");
    }
}
