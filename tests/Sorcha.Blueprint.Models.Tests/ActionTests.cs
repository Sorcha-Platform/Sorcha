// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Models.Tests;

public class ActionTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var action = new Models.Action();

        // Assert
        action.Id.Should().Be(0);
        action.PreviousTxId.Should().BeEmpty();
        action.BlueprintId.Should().BeEmpty();
        action.Title.Should().BeEmpty();
        action.Description.Should().BeEmpty();
        action.Sender.Should().BeEmpty();
        action.Participants.Should().NotBeNull().And.BeEmpty();
        action.RequiredActionData.Should().NotBeNull().And.BeEmpty();
        action.AdditionalRecipients.Should().NotBeNull().And.BeEmpty();
        action.Disclosures.Should().NotBeNull().And.BeEmpty();
        action.PreviousData.Should().BeNull();
        action.DataSchemas.Should().BeNull();
        action.Condition.Should().NotBeNull();
        action.Calculations.Should().NotBeNull().And.BeEmpty();
        action.Form.Should().NotBeNull();
    }

    [Fact]
    public void Id_ShouldAcceptValidValue()
    {
        // Arrange
        var action = new Models.Action();

        // Act
        action.Id = 5;

        // Assert
        action.Id.Should().Be(5);
    }

    [Fact]
    public void Title_ShouldAcceptValidValue()
    {
        // Arrange
        var action = new Models.Action();
        var title = "Submit Application";

        // Act
        action.Title = title;

        // Assert
        action.Title.Should().Be(title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Title_WithEmptyValue_ShouldFailValidation(string? title)
    {
        // Arrange
        var action = new Models.Action { Id = 0, Title = title! };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Title"));
    }

    [Fact]
    public void Title_TooLong_ShouldFailValidation()
    {
        // Arrange
        var action = new Models.Action
        {
            Id = 0,
            Title = new string('a', 101)
        };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Title"));
    }

    [Fact]
    public void Description_ShouldAcceptValidValue()
    {
        // Arrange
        var action = new Models.Action();
        var description = "This action submits the application for review";

        // Act
        action.Description = description;

        // Assert
        action.Description.Should().Be(description);
    }

    [Fact]
    public void Description_CanBeEmpty()
    {
        // Arrange
        var action = new Models.Action
        {
            Id = 0,
            Title = "Valid Title",
            Description = "",
            Disclosures = new List<Disclosure>
            {
                new Disclosure { ParticipantAddress = "p1", DataPointers = new List<string> { "/data" } }
            }
        };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Description_TooLong_ShouldFailValidation()
    {
        // Arrange
        var action = new Models.Action
        {
            Id = 0,
            Title = "Valid Title",
            Description = new string('a', 2049)
        };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains("Description"));
    }

    [Fact]
    public void Sender_ShouldAcceptValidValue()
    {
        // Arrange
        var action = new Models.Action();
        var sender = "participant-001";

        // Act
        action.Sender = sender;

        // Assert
        action.Sender.Should().Be(sender);
    }

    [Fact]
    public void Sender_CanBeEmpty()
    {
        // Arrange
        var action = new Models.Action
        {
            Id = 0,
            Title = "Valid Title",
            Sender = "",
            Disclosures = new List<Disclosure>
            {
                new Disclosure { ParticipantAddress = "p1", DataPointers = new List<string> { "/data" } }
            }
        };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Participants_ShouldAcceptConditionsList()
    {
        // Arrange
        var action = new Models.Action();
        var participants = new List<Condition>
        {
            new Condition("participant-1", true),
            new Condition("participant-2", false)
        };

        // Act
        action.Participants = participants;

        // Assert
        action.Participants.Should().HaveCount(2);
        action.Participants.Should().Contain(p => p.Principal == "participant-1");
    }

    [Fact]
    public void RequiredActionData_ShouldAcceptStringList()
    {
        // Arrange
        var action = new Models.Action();
        var requiredData = new List<string> { "schema-1", "schema-2" };

        // Act
        action.RequiredActionData = requiredData;

        // Assert
        action.RequiredActionData.Should().HaveCount(2);
        action.RequiredActionData.Should().Contain("schema-1");
    }

    [Fact]
    public void AdditionalRecipients_ShouldAcceptStringList()
    {
        // Arrange
        var action = new Models.Action();
        var recipients = new List<string> { "recipient-1", "recipient-2" };

        // Act
        action.AdditionalRecipients = recipients;

        // Assert
        action.AdditionalRecipients.Should().HaveCount(2);
        action.AdditionalRecipients.Should().Contain("recipient-1");
    }

    [Fact]
    public void Disclosures_ShouldAcceptDisclosureList()
    {
        // Arrange
        var action = new Models.Action();
        var disclosures = new List<Disclosure>
        {
            new Disclosure
            {
                ParticipantAddress = "p1",
                DataPointers = new List<string> { "/field1", "/field2" }
            }
        };

        // Act
        action.Disclosures = disclosures;

        // Assert
        action.Disclosures.Should().HaveCount(1);
        action.Disclosures.First().ParticipantAddress.Should().Be("p1");
    }

    [Fact]
    public void PreviousData_ShouldAcceptJsonDocument()
    {
        // Arrange
        var action = new Models.Action();
        var json = "{\"key\":\"value\"}";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        action.PreviousData = jsonDoc;

        // Assert
        action.PreviousData.Should().NotBeNull();
        action.PreviousData!.RootElement.GetProperty("key").GetString().Should().Be("value");
    }

    [Fact]
    public void DataSchemas_ShouldAcceptJsonDocumentList()
    {
        // Arrange
        var action = new Models.Action();
        var schemas = new List<JsonDocument>
        {
            JsonDocument.Parse("{\"type\":\"object\"}"),
            JsonDocument.Parse("{\"type\":\"string\"}")
        };

        // Act
        action.DataSchemas = schemas;

        // Assert
        action.DataSchemas.Should().HaveCount(2);
    }

    [Fact]
    public void Condition_ShouldHaveDefaultValue()
    {
        // Act
        var action = new Models.Action();

        // Assert
        action.Condition.Should().NotBeNull();
        action.Condition.ToString().Should().Contain("==");
    }

    [Fact]
    public void Condition_ShouldAcceptJsonNode()
    {
        // Arrange
        var action = new Models.Action();
        var condition = JsonNode.Parse("{\">\": [5, 3]}");

        // Act
        action.Condition = condition;

        // Assert
        action.Condition.Should().NotBeNull();
        // Verify the JSON object has the expected key
        var jsonObj = action.Condition!.AsObject();
        jsonObj.ContainsKey(">").Should().BeTrue();
    }

    [Fact]
    public void Calculations_ShouldAcceptDictionary()
    {
        // Arrange
        var action = new Models.Action();
        var calculations = new Dictionary<string, JsonNode>
        {
            { "total", JsonNode.Parse("{\"*\": [5, 3]}") }
        };

        // Act
        action.Calculations = calculations;

        // Assert
        action.Calculations.Should().ContainKey("total");
    }

    [Fact]
    public void Form_ShouldHaveDefaultValue()
    {
        // Act
        var action = new Models.Action();

        // Assert
        action.Form.Should().NotBeNull();
        action.Form!.ControlType.Should().Be(ControlTypes.Layout);
        action.Form.Layout.Should().Be(LayoutTypes.VerticalLayout);
    }

    [Fact]
    public void Form_ShouldAcceptCustomControl()
    {
        // Arrange
        var action = new Models.Action();
        var form = new Control
        {
            ControlType = ControlTypes.Layout,
            Layout = LayoutTypes.HorizontalLayout
        };

        // Act
        action.Form = form;

        // Assert
        action.Form.Should().NotBeNull();
        action.Form!.Layout.Should().Be(LayoutTypes.HorizontalLayout);
    }

    [Fact]
    public void WithCompleteData_ShouldPassValidation()
    {
        // Arrange
        var action = new Models.Action
        {
            Id = 0,
            Title = "Submit Application",
            Description = "Submit the application for review",
            Sender = "applicant",
            Participants = new List<Condition>
            {
                new Condition("reviewer", true)
            },
            Disclosures = new List<Disclosure>
            {
                new Disclosure
                {
                    ParticipantAddress = "reviewer",
                    DataPointers = new List<string> { "/applicationData" }
                }
            }
        };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void WithMinimalData_ShouldPassValidation()
    {
        // Arrange
        var action = new Models.Action
        {
            Id = 0,
            Title = "Action Title",
            Disclosures = new List<Disclosure>
            {
                new Disclosure { ParticipantAddress = "p1", DataPointers = new List<string> { "/data" } }
            }
        };
        var context = new ValidationContext(action);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(action, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }
}
