// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Xunit;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Tests for disclosure validation rules at the Blueprint/Action level.
/// Tests Category 5 from BLUEPRINT-VALIDATION-TEST-PLAN.md
/// </summary>
public class DisclosureValidationTests
{
    #region Test Data Helpers

    private static BpModels.Blueprint CreateValidBlueprint()
    {
        return new BpModels.Blueprint
        {
            Id = "test-blueprint-001",
            Title = "Test Blueprint",
            Description = "A test blueprint for disclosure validation",
            Version = 1,
            Participants = new List<BpModels.Participant>
            {
                new() { Id = "participant1", Name = "Alice", Organisation = "Acme Corp", WalletAddress = "wallet1" },
                new() { Id = "participant2", Name = "Bob", Organisation = "Beta Inc", WalletAddress = "wallet2" },
                new() { Id = "participant3", Name = "Charlie", Organisation = "Gamma LLC", WalletAddress = "wallet3" }
            },
            Actions = new List<BpModels.Action>
            {
                new()
                {
                    Id = 0,
                    Title = "Submit Application",
                    Sender = "participant1",
                    Disclosures = new List<BpModels.Disclosure>
                    {
                        new("participant2", new List<string> { "/name", "/email" })
                    }
                }
            }
        };
    }

    private static JsonDocument CreateSampleDataSchema()
    {
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string" },
                email = new { type = "string", format = "email" },
                age = new { type = "integer" },
                address = new
                {
                    type = "object",
                    properties = new
                    {
                        street = new { type = "string" },
                        city = new { type = "string" }
                    }
                }
            },
            required = new[] { "name", "email" }
        });
        return JsonDocument.Parse(schema);
    }

    #endregion

    #region 5.1 Disclosure Requirement

    [Fact]
    public void Action_WithZeroDisclosures_ShouldFailValidation()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var action = blueprint.Actions[0];
        action.Disclosures = new List<BpModels.Disclosure>(); // Empty disclosures

        // Act & Assert
        // The [DataAnnotations.MinLength(1)] on Action.Disclosures should enforce this
        action.Disclosures.Should().BeEmpty("Setting up empty disclosures for test");

        // Validate only the Disclosures property
        var propertyInfo = typeof(BpModels.Action).GetProperty("Disclosures");
        var attributes = propertyInfo!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute), false);

        // Assert
        attributes.Should().NotBeEmpty("Disclosures property should have MinLength attribute");
        var minLengthAttr = attributes[0] as System.ComponentModel.DataAnnotations.MinLengthAttribute;
        minLengthAttr!.Length.Should().Be(1, "Minimum 1 disclosure required");
        action.Disclosures.Count().Should().BeLessThan(minLengthAttr.Length);
    }

    [Fact]
    public void Action_WithOneDisclosure_ShouldPassValidation()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var action = blueprint.Actions[0];
        action.Disclosures = new List<BpModels.Disclosure>
        {
            new("participant2", new List<string> { "/name" })
        };

        // Act - Check disclosure count meets minimum requirement
        var propertyInfo = typeof(BpModels.Action).GetProperty("Disclosures");
        var attributes = propertyInfo!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute), false);
        var minLengthAttr = attributes[0] as System.ComponentModel.DataAnnotations.MinLengthAttribute;

        // Assert
        action.Disclosures.Should().HaveCount(1);
        action.Disclosures.Count().Should().BeGreaterThanOrEqualTo(minLengthAttr!.Length, "Should meet minimum disclosure requirement");
    }

    [Fact]
    public void Action_WithMultipleDisclosures_ShouldPassValidation()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var action = blueprint.Actions[0];
        action.Disclosures = new List<BpModels.Disclosure>
        {
            new("participant2", new List<string> { "/name", "/email" }),
            new("participant3", new List<string> { "/name" }),
            new("participant1", new List<string> { "/*" }) // All fields
        };

        // Act - Check disclosure count meets minimum requirement
        var propertyInfo = typeof(BpModels.Action).GetProperty("Disclosures");
        var attributes = propertyInfo!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute), false);
        var minLengthAttr = attributes[0] as System.ComponentModel.DataAnnotations.MinLengthAttribute;

        // Assert
        action.Disclosures.Should().HaveCount(3);
        action.Disclosures.Count().Should().BeGreaterThanOrEqualTo(minLengthAttr!.Length, "Should meet minimum disclosure requirement");
    }

    [Fact]
    public void Action_MinimumDisclosureCount_ShouldBeOne()
    {
        // Arrange
        var action = new BpModels.Action
        {
            Id = 0,
            Title = "Test Action",
            Sender = "participant1",
            Disclosures = new List<BpModels.Disclosure>()
        };

        // Act - Verify the MinLength attribute enforces minimum of 1
        var propertyInfo = typeof(BpModels.Action).GetProperty("Disclosures");
        var attributes = propertyInfo!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.MinLengthAttribute), false);

        // Assert
        attributes.Should().NotBeEmpty("Disclosures property should have MinLength attribute");
        var minLengthAttr = attributes[0] as System.ComponentModel.DataAnnotations.MinLengthAttribute;
        minLengthAttr!.Length.Should().Be(1, "Minimum disclosure count should be 1");
        action.Disclosures.Should().BeEmpty("Test setup: empty disclosures");
    }

    #endregion

    #region 5.2 Disclosure Target Validation

    [Fact]
    public void Disclosure_WithValidParticipantAddress_ShouldBeValid()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var validParticipantAddress = blueprint.Participants[1].WalletAddress; // "wallet2"

        var disclosure = new BpModels.Disclosure(validParticipantAddress, new List<string> { "/name" });

        // Act
        var isValid = blueprint.Participants.Any(p => p.WalletAddress == disclosure.ParticipantAddress);

        // Assert
        isValid.Should().BeTrue("Disclosure should reference an existing participant wallet address");
        disclosure.ParticipantAddress.Should().Be("wallet2");
    }

    [Fact]
    public void Disclosure_WithNonExistentParticipant_ShouldFailValidation()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var invalidParticipantAddress = "non-existent-wallet";

        var disclosure = new BpModels.Disclosure(invalidParticipantAddress, new List<string> { "/name" });

        // Act
        var isValid = blueprint.Participants.Any(p => p.WalletAddress == disclosure.ParticipantAddress);

        // Assert
        isValid.Should().BeFalse("Disclosure referencing non-existent participant should fail");
        blueprint.Participants.Should().NotContain(p => p.WalletAddress == invalidParticipantAddress);
    }

    [Fact]
    public void Disclosure_WithEmptyParticipantAddress_ShouldFailValidation()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("", new List<string> { "/name" });

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("Disclosure with empty participant address should fail");
        validationResults.Should().Contain(r => r.MemberNames.Contains("ParticipantAddress"));
    }

    [Fact]
    public void Disclosure_WithAllParticipants_ShouldBeValid()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var disclosures = blueprint.Participants.Select(p =>
            new BpModels.Disclosure(p.WalletAddress, new List<string> { "/*" })
        ).ToList();

        // Act
        var allValid = disclosures.All(d =>
            blueprint.Participants.Any(p => p.WalletAddress == d.ParticipantAddress));

        // Assert
        allValid.Should().BeTrue("Disclosures to all participants should be valid");
        disclosures.Should().HaveCount(blueprint.Participants.Count);
    }

    [Fact]
    public void Disclosure_ParticipantAddress_MaxLength100_ShouldBeEnforced()
    {
        // Arrange
        var longAddress = new string('A', 101); // 101 characters
        var disclosure = new BpModels.Disclosure(longAddress, new List<string> { "/name" });

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("Participant address over 100 chars should fail validation");
        validationResults.Should().Contain(r => r.MemberNames.Contains("ParticipantAddress"));
    }

    [Fact]
    public void Disclosure_MultipleParticipants_DifferentDataPointers_ShouldBeValid()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var action = blueprint.Actions[0];
        action.Disclosures = new List<BpModels.Disclosure>
        {
            new(blueprint.Participants[0].WalletAddress, new List<string> { "/name", "/email" }),
            new(blueprint.Participants[1].WalletAddress, new List<string> { "/name" }),
            new(blueprint.Participants[2].WalletAddress, new List<string> { "/*" })
        };

        // Act
        var allValid = action.Disclosures.All(d =>
            blueprint.Participants.Any(p => p.WalletAddress == d.ParticipantAddress));

        // Assert
        allValid.Should().BeTrue("All disclosures should reference valid participants");
        action.Disclosures.Should().HaveCount(3);
    }

    #endregion

    #region 5.3 Disclosure Data Fields

    [Fact]
    public void Disclosure_WithValidDataPointers_ShouldBeValid()
    {
        // Arrange
        var dataSchema = CreateSampleDataSchema();
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "/name", "/email", "/age" });

        // Act - Check if pointers reference valid schema fields
        var schemaRoot = dataSchema.RootElement;
        var properties = schemaRoot.GetProperty("properties");
        var allPointersValid = disclosure.DataPointers
            .Where(p => p != "/*" && p.StartsWith("/"))
            .Select(p => p.TrimStart('/'))
            .All(fieldName => properties.TryGetProperty(fieldName, out _));

        // Assert
        allPointersValid.Should().BeTrue("All data pointers should reference valid schema fields");
        disclosure.DataPointers.Should().Contain("/name");
        disclosure.DataPointers.Should().Contain("/email");
    }

    [Fact]
    public void Disclosure_WithNonExistentDataPointer_ShouldFailValidation()
    {
        // Arrange
        var dataSchema = CreateSampleDataSchema();
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "/nonExistentField" });

        // Act
        var schemaRoot = dataSchema.RootElement;
        var properties = schemaRoot.GetProperty("properties");
        var fieldName = disclosure.DataPointers[0].TrimStart('/');
        var isValid = properties.TryGetProperty(fieldName, out _);

        // Assert
        isValid.Should().BeFalse("Data pointer to non-existent field should fail validation");
        disclosure.DataPointers[0].Should().Be("/nonExistentField");
    }

    [Fact]
    public void Disclosure_WithWildcardPointer_ShouldBeValid()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "/*" });

        // Act & Assert
        disclosure.DataPointers.Should().Contain("/*");
        disclosure.DataPointers.Should().HaveCount(1);
    }

    [Fact]
    public void Disclosure_WithHashWildcardPointer_ShouldBeValid()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "#/*" });

        // Act & Assert
        disclosure.DataPointers.Should().Contain("#/*");
        disclosure.DataPointers.Should().HaveCount(1);
    }

    [Fact]
    public void Disclosure_WithNestedDataPointer_ShouldBeValid()
    {
        // Arrange
        var dataSchema = CreateSampleDataSchema();
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "/address/city", "/address/street" });

        // Act
        var schemaRoot = dataSchema.RootElement;
        var properties = schemaRoot.GetProperty("properties");
        var addressProperty = properties.GetProperty("address");
        var addressProps = addressProperty.GetProperty("properties");

        var cityExists = addressProps.TryGetProperty("city", out _);
        var streetExists = addressProps.TryGetProperty("street", out _);

        // Assert
        cityExists.Should().BeTrue("Nested /address/city pointer should be valid");
        streetExists.Should().BeTrue("Nested /address/street pointer should be valid");
        disclosure.DataPointers.Should().Contain("/address/city");
    }

    [Fact]
    public void Disclosure_WithEmptyDataPointers_ShouldFailValidation()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("participant1", new List<string>());

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("Disclosure with empty dataPointers should fail");
        validationResults.Should().Contain(r => r.MemberNames.Contains("DataPointers"));
    }

    [Fact]
    public void Disclosure_DataPointers_MinLength1_ShouldBeEnforced()
    {
        // Arrange
        var disclosure = new Disclosure
        {
            ParticipantAddress = "participant1",
            DataPointers = new List<string>() // Empty list
        };

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("DataPointers must have at least 1 element");
        disclosure.DataPointers.Should().BeEmpty();
    }

    [Fact]
    public void Disclosure_WithMultipleDataPointers_ShouldBeValid()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("participant1", new List<string>
        {
            "/name",
            "/email",
            "/age",
            "/address/city"
        });

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("Disclosure with multiple valid data pointers should be valid");
        disclosure.DataPointers.Should().HaveCount(4);
    }

    [Fact]
    public void Disclosure_WithJSONPointerEscaping_ShouldBeValid()
    {
        // Arrange - field names with special characters
        var disclosure = new BpModels.Disclosure("participant1", new List<string>
        {
            "/a~1b",  // JSON Pointer for field "a/b" (/ becomes ~1)
            "/a~0b"   // JSON Pointer for field "a~b" (~ becomes ~0)
        });

        // Act & Assert
        disclosure.DataPointers.Should().Contain("/a~1b");
        disclosure.DataPointers.Should().Contain("/a~0b");
        disclosure.DataPointers.Should().HaveCount(2);
    }

    [Fact]
    public void Disclosure_WithHashPrefix_ShouldBeValid()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "#/field1", "#/field2" });

        // Act & Assert
        disclosure.DataPointers.Should().Contain("#/field1");
        disclosure.DataPointers.Should().Contain("#/field2");
        disclosure.DataPointers.All(p => p.StartsWith("#/")).Should().BeTrue();
    }

    #endregion

    #region Integration: Blueprint-Level Disclosure Validation

    [Fact]
    public void Blueprint_AllActionsHaveValidDisclosures_ShouldPassValidation()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        blueprint.Actions = new List<BpModels.Action>
        {
            new()
            {
                Id = 0,
                Title = "Action 1",
                Sender = "participant1",
                Disclosures = new List<BpModels.Disclosure>
                {
                    new(blueprint.Participants[1].WalletAddress, new List<string> { "/name" })
                }
            },
            new()
            {
                Id = 1,
                Title = "Action 2",
                Sender = "participant2",
                Disclosures = new List<BpModels.Disclosure>
                {
                    new(blueprint.Participants[0].WalletAddress, new List<string> { "/email" }),
                    new(blueprint.Participants[2].WalletAddress, new List<string> { "/*" })
                }
            }
        };

        // Act - Check all disclosures reference valid participants
        var allDisclosuresValid = blueprint.Actions.All(action =>
            action.Disclosures.All(d =>
                blueprint.Participants.Any(p => p.WalletAddress == d.ParticipantAddress)));

        // Assert
        allDisclosuresValid.Should().BeTrue("All action disclosures should reference valid participants");
        blueprint.Actions.Should().HaveCount(2);
        blueprint.Actions[1].Disclosures.Should().HaveCount(2);
    }

    [Fact]
    public void Blueprint_ActionWithInvalidDisclosureParticipant_ShouldFailValidation()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        blueprint.Actions[0].Disclosures = new List<BpModels.Disclosure>
        {
            new("invalid-wallet-address", new List<string> { "/name" })
        };

        // Act
        var allDisclosuresValid = blueprint.Actions.All(action =>
            action.Disclosures.All(d =>
                blueprint.Participants.Any(p => p.WalletAddress == d.ParticipantAddress)));

        // Assert
        allDisclosuresValid.Should().BeFalse("Action with invalid disclosure participant should fail");
    }

    [Fact]
    public void Blueprint_SelfDisclosure_ShouldBeValid()
    {
        // Arrange
        var blueprint = CreateValidBlueprint();
        var action = blueprint.Actions[0];
        action.Sender = blueprint.Participants[0].WalletAddress!;
        action.Disclosures = new List<BpModels.Disclosure>
        {
            // Sender can see their own data
            new(blueprint.Participants[0].WalletAddress, new List<string> { "/*" }),
            // Plus disclosure to another participant
            new(blueprint.Participants[1].WalletAddress, new List<string> { "/name" })
        };

        // Act
        var senderParticipant = blueprint.Participants.FirstOrDefault(p => p.WalletAddress == action.Sender);
        var hasSelfDisclosure = action.Disclosures.Any(d => d.ParticipantAddress == action.Sender);

        // Assert
        senderParticipant.Should().NotBeNull("Sender should be a valid participant");
        hasSelfDisclosure.Should().BeTrue("Self-disclosure should be valid");
        action.Disclosures.Should().HaveCount(2);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Disclosure_WithWhitespaceInDataPointer_ShouldStillValidate()
    {
        // Arrange
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "  /name  " });

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("Disclosure is valid even with whitespace in pointer");
        disclosure.DataPointers[0].Should().Be("  /name  ");
    }

    [Fact]
    public void Disclosure_WithDuplicateDataPointers_ShouldBeValid()
    {
        // Arrange - duplicates are allowed (will be deduplicated during processing)
        var disclosure = new BpModels.Disclosure("participant1", new List<string> { "/name", "/name", "/email" });

        // Act
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(disclosure);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            disclosure, validationContext, validationResults, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("Duplicate data pointers are allowed");
        disclosure.DataPointers.Should().HaveCount(3);
    }

    [Fact]
    public void Disclosure_Constructor_ShouldSetProperties()
    {
        // Arrange & Act
        var participantAddr = "wallet123";
        var pointers = new List<string> { "/field1", "/field2" };
        var disclosure = new BpModels.Disclosure(participantAddr, pointers);

        // Assert
        disclosure.ParticipantAddress.Should().Be(participantAddr);
        disclosure.DataPointers.Should().BeEquivalentTo(pointers);
        disclosure.DataPointers.Should().HaveCount(2);
    }

    [Fact]
    public void Disclosure_DefaultConstructor_ShouldInitializeEmptyCollections()
    {
        // Arrange & Act
        var disclosure = new BpModels.Disclosure();

        // Assert
        disclosure.ParticipantAddress.Should().BeEmpty();
        disclosure.DataPointers.Should().NotBeNull();
        disclosure.DataPointers.Should().BeEmpty();
    }

    #endregion
}
