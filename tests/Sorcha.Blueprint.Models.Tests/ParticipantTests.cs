// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models.Tests;

public class ParticipantTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var participant = new Participant();

        // Assert
        participant.Id.Should().NotBeEmpty("Id is auto-generated as a GUID");
        participant.Name.Should().BeEmpty();
        participant.Organisation.Should().BeEmpty();
        participant.WalletAddress.Should().BeNull();
        participant.DidUri.Should().BeNull();
        participant.UseStealthAddress.Should().BeFalse();
    }

    [Fact]
    public void Id_ShouldAcceptValidValue()
    {
        // Arrange
        var participant = new Participant();
        var id = "participant-001";

        // Act
        participant.Id = id;

        // Assert
        participant.Id.Should().Be(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Id_WithEmptyValue_ShouldFailValidation(string? id)
    {
        // Arrange
        var participant = new Participant { Id = id!, Name = "Test Participant" };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Id"));
    }

    [Fact]
    public void Id_TooLong_ShouldFailValidation()
    {
        // Arrange
        var participant = new Participant
        {
            Id = new string('a', 65),
            Name = "Test Participant"
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Id"));
    }

    [Fact]
    public void Name_ShouldAcceptValidValue()
    {
        // Arrange
        var participant = new Participant();
        var name = "John Doe";

        // Act
        participant.Name = name;

        // Assert
        participant.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Name_WithEmptyValue_ShouldFailValidation(string? name)
    {
        // Arrange
        var participant = new Participant { Id = "p1", Name = name! };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Name"));
    }

    [Fact]
    public void Name_Empty_ShouldFailValidation()
    {
        // Arrange - Name requires MinLength(1)
        var participant = new Participant { Id = "p1", Name = "" };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Name"));
    }

    [Fact]
    public void Name_TooLong_ShouldFailValidation()
    {
        // Arrange
        var participant = new Participant
        {
            Id = "p1",
            Name = new string('a', 201)
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Name"));
    }

    [Fact]
    public void Organisation_ShouldAcceptValidValue()
    {
        // Arrange
        var participant = new Participant();
        var org = "Acme Corporation";

        // Act
        participant.Organisation = org;

        // Assert
        participant.Organisation.Should().Be(org);
    }

    [Fact]
    public void Organisation_CanBeEmpty()
    {
        // Arrange
        var participant = new Participant
        {
            Id = "p1",
            Name = "John Doe",
            Organisation = ""
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void WalletAddress_ShouldAcceptValidValue()
    {
        // Arrange
        var participant = new Participant();
        var address = "0x1234567890abcdef";

        // Act
        participant.WalletAddress = address;

        // Assert
        participant.WalletAddress.Should().Be(address);
    }

    [Fact]
    public void WalletAddress_CanBeEmpty()
    {
        // Arrange
        var participant = new Participant
        {
            Id = "p1",
            Name = "John Doe",
            WalletAddress = ""
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void DidUri_ShouldAcceptValidValue()
    {
        // Arrange
        var participant = new Participant();
        var did = "did:example:123456789abcdefghi";

        // Act
        participant.DidUri = did;

        // Assert
        participant.DidUri.Should().Be(did);
    }

    [Fact]
    public void DidUri_CanBeNull()
    {
        // Arrange
        var participant = new Participant
        {
            Id = "p1",
            Name = "John Doe",
            DidUri = null
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UseStealthAddress_ShouldAcceptBooleanValues(bool useStealthAddress)
    {
        // Arrange
        var participant = new Participant();

        // Act
        participant.UseStealthAddress = useStealthAddress;

        // Assert
        participant.UseStealthAddress.Should().Be(useStealthAddress);
    }

    [Fact]
    public void UseStealthAddress_ShouldDefaultToFalse()
    {
        // Act
        var participant = new Participant();

        // Assert
        participant.UseStealthAddress.Should().BeFalse();
    }

    [Fact]
    public void Equals_WithSameId_ShouldReturnTrue()
    {
        // Arrange
        var participant1 = new Participant
        {
            Id = "p1",
            Name = "John Doe"
        };
        var participant2 = new Participant
        {
            Id = "p1",
            Name = "John Doe"
        };

        // Act & Assert
        participant1.Equals(participant2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentId_ShouldReturnFalse()
    {
        // Arrange
        var participant1 = new Participant
        {
            Id = "p1",
            Name = "John Doe"
        };
        var participant2 = new Participant
        {
            Id = "p2",
            Name = "John Doe"
        };

        // Act & Assert
        participant1.Equals(participant2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        // Arrange
        var participant = new Participant { Id = "p1", Name = "Test" };

        // Act & Assert
        participant.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void WithCompleteData_ShouldPassValidation()
    {
        // Arrange
        var participant = new Participant
        {
            Id = "participant-001",
            Name = "John Doe",
            Organisation = "Acme Corp",
            WalletAddress = "0x1234567890abcdef",
            DidUri = "did:example:123456789",
            UseStealthAddress = true
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void WithMinimalData_ShouldPassValidation()
    {
        // Arrange
        var participant = new Participant
        {
            Id = "p1",
            Name = "John Doe"
        };
        var context = new ValidationContext(participant);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(participant, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
