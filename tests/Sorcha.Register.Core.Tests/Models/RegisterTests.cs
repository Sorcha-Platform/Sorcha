// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using System.ComponentModel.DataAnnotations;
using Xunit;
using RegisterModel = Sorcha.Register.Models.Register;

namespace Sorcha.Register.Core.Tests.Models;

public class RegisterTests
{
    [Fact]
    public void Register_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var register = new RegisterModel();

        // Assert
        register.Id.Should().BeEmpty();
        register.Name.Should().BeEmpty();
        register.Height.Should().Be(0u);
        register.Status.Should().Be(RegisterStatus.Offline);
        register.Advertise.Should().BeFalse();
        register.IsFullReplica.Should().BeTrue();
        register.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        register.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        register.Votes.Should().BeNull();
    }

    [Fact]
    public void Register_WithValidProperties_ShouldCreateSuccessfully()
    {
        // Arrange
        var id = Guid.NewGuid().ToString("N");
        var name = "TestRegister";
        var createdAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var register = new RegisterModel
        {
            Id = id,
            Name = name,
            Height = 5,
            Status = RegisterStatus.Online,
            Advertise = true,
            IsFullReplica = false,
            CreatedAt = createdAt,
            UpdatedAt = DateTime.UtcNow,
            Votes = "some-votes"
        };

        // Assert
        register.Id.Should().Be(id);
        register.Name.Should().Be(name);
        register.Height.Should().Be(5u);
        register.Status.Should().Be(RegisterStatus.Online);
        register.Advertise.Should().BeTrue();
        register.IsFullReplica.Should().BeFalse();
        register.CreatedAt.Should().Be(createdAt);
        register.Votes.Should().Be("some-votes");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Register_WithInvalidId_ShouldFailValidation(string? invalidId)
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = invalidId!,
            Name = "TestRegister"
        };

        // Act
        var validationResults = ValidateModel(register);

        // Assert
        validationResults.Should().ContainSingle(v => v.MemberNames.Contains("Id"));
    }

    [Fact]
    public void Register_WithIdLengthNot32_ShouldFailValidation()
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = "tooshort",
            Name = "TestRegister"
        };

        // Act
        var validationResults = ValidateModel(register);

        // Assert
        validationResults.Should().ContainSingle(v => v.MemberNames.Contains("Id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Register_WithInvalidName_ShouldFailValidation(string? invalidName)
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = invalidName!
        };

        // Act
        var validationResults = ValidateModel(register);

        // Assert
        validationResults.Should().ContainSingle(v => v.MemberNames.Contains("Name"));
    }

    [Fact]
    public void Register_WithNameTooLong_ShouldFailValidation()
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = new string('a', 39) // Max is 38
        };

        // Act
        var validationResults = ValidateModel(register);

        // Assert
        validationResults.Should().ContainSingle(v => v.MemberNames.Contains("Name"));
    }

    [Theory]
    [InlineData(RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Online)]
    [InlineData(RegisterStatus.Checking)]
    [InlineData(RegisterStatus.Recovery)]
    public void Register_WithAllStatusValues_ShouldBeValid(RegisterStatus status)
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "TestRegister",
            Status = status
        };

        // Act
        var validationResults = ValidateModel(register);

        // Assert
        validationResults.Should().BeEmpty();
    }

    [Fact]
    public void Register_HeightProperty_ShouldAcceptUInt32Values()
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "TestRegister"
        };

        // Act
        register.Height = 0;
        register.Height.Should().Be(0u);

        register.Height = 1000;
        register.Height.Should().Be(1000u);

        register.Height = uint.MaxValue;
        register.Height.Should().Be(uint.MaxValue);
    }

    [Fact]
    public void Register_FullyPopulated_ShouldPassValidation()
    {
        // Arrange
        var register = new RegisterModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Production Register",
            Height = 1000,
            Status = RegisterStatus.Online,
            Advertise = true,
            IsFullReplica = true,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            UpdatedAt = DateTime.UtcNow,
            Votes = "{\"vote1\": true}"
        };

        // Act
        var validationResults = ValidateModel(register);

        // Assert
        validationResults.Should().BeEmpty();
    }

    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, validationContext, validationResults, true);
        return validationResults;
    }
}
