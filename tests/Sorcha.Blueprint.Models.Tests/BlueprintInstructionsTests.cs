// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Models.Tests;

public class BlueprintInstructionsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void BlueprintInstructions_DefaultsToNull()
    {
        var instructions = new BlueprintInstructions();

        instructions.Overview.Should().BeNull();
        instructions.Locale.Should().BeNull();
        instructions.ActionInstructions.Should().BeNull();
        instructions.ParticipantInstructions.Should().BeNull();
        instructions.InstructionSets.Should().BeNull();
        instructions.GovernanceRoles.Should().BeNull();
    }

    [Fact]
    public void BlueprintInstructions_RoundTrip_PreservesAllFields()
    {
        var instructions = new BlueprintInstructions
        {
            Overview = "## Purpose\nThis workflow manages approvals.",
            Locale = "en-GB",
            ActionInstructions = new Dictionary<int, string>
            {
                [0] = "Fill in the request form.",
                [1] = "Review the submission."
            },
            ParticipantInstructions = new Dictionary<string, string>
            {
                ["applicant"] = "You are submitting a request.",
                ["reviewer"] = "Check compliance guidelines."
            },
            InstructionSets =
            [
                new InstructionSet { Locale = "fr-FR", Source = "did:sorcha:instructions/bp-001/fr", Version = "1.0" }
            ],
            GovernanceRoles = new Dictionary<string, string>
            {
                ["reviewer"] = "did:sorcha:participant:reviewer-001",
                ["publisher"] = "did:sorcha:participant:publisher-001"
            }
        };

        var json = JsonSerializer.Serialize(instructions, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BlueprintInstructions>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Overview.Should().Be(instructions.Overview);
        deserialized.Locale.Should().Be("en-GB");
        deserialized.ActionInstructions.Should().HaveCount(2);
        deserialized.ActionInstructions![0].Should().Be("Fill in the request form.");
        deserialized.ParticipantInstructions.Should().HaveCount(2);
        deserialized.InstructionSets.Should().HaveCount(1);
        deserialized.InstructionSets![0].Locale.Should().Be("fr-FR");
        deserialized.GovernanceRoles.Should().HaveCount(2);
    }

    [Fact]
    public void InstructionSet_DefaultValues()
    {
        var set = new InstructionSet();

        set.Locale.Should().BeEmpty();
        set.Source.Should().BeEmpty();
        set.Version.Should().BeNull();
    }

    [Fact]
    public void InstructionSet_RoundTrip()
    {
        var set = new InstructionSet
        {
            Locale = "de-DE",
            Source = "https://example.com/instructions/de",
            Version = "2.1"
        };

        var json = JsonSerializer.Serialize(set, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<InstructionSet>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Locale.Should().Be("de-DE");
        deserialized.Source.Should().Be("https://example.com/instructions/de");
        deserialized.Version.Should().Be("2.1");
    }

    [Fact]
    public void BlueprintVersion_DefaultValues()
    {
        var version = new BlueprintVersion();

        version.Major.Should().Be(0);
        version.Minor.Should().Be(0);
        version.ChangeType.Should().Be("structural");
        version.StructuralHash.Should().BeEmpty();
        version.PublishedBy.Should().BeEmpty();
        version.TransactionId.Should().BeEmpty();
    }

    [Fact]
    public void BlueprintVersion_ToString_FormatsCorrectly()
    {
        var version = new BlueprintVersion { Major = 2, Minor = 1 };
        version.ToString().Should().Be("v2.1");
    }

    [Fact]
    public void BlueprintVersion_RoundTrip()
    {
        var version = new BlueprintVersion
        {
            Major = 3,
            Minor = 5,
            ChangeType = "documentation",
            StructuralHash = new string('a', 64),
            PublishedAt = new DateTimeOffset(2026, 3, 16, 14, 30, 0, TimeSpan.Zero),
            PublishedBy = "wallet-abc123",
            TransactionId = "tx-001"
        };

        var json = JsonSerializer.Serialize(version, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BlueprintVersion>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Major.Should().Be(3);
        deserialized.Minor.Should().Be(5);
        deserialized.ChangeType.Should().Be("documentation");
        deserialized.StructuralHash.Should().HaveLength(64);
        deserialized.PublishedBy.Should().Be("wallet-abc123");
    }

    [Fact]
    public void Blueprint_InstructionsProperty_NullByDefault()
    {
        var blueprint = new Models.Blueprint();

        blueprint.Instructions.Should().BeNull();
    }

    [Fact]
    public void Blueprint_NullInstructions_OmittedFromJson()
    {
        var blueprint = new Models.Blueprint
        {
            Title = "Test",
            Description = "A test blueprint"
        };

        var json = JsonSerializer.Serialize(blueprint, JsonOptions);

        json.Should().NotContain("\"instructions\"");
    }

    [Fact]
    public void Blueprint_WithInstructions_IncludedInJson()
    {
        var blueprint = new Models.Blueprint
        {
            Title = "Test",
            Description = "A test blueprint",
            Instructions = new BlueprintInstructions
            {
                Overview = "Test overview"
            }
        };

        var json = JsonSerializer.Serialize(blueprint, JsonOptions);

        json.Should().Contain("\"instructions\"");
        json.Should().Contain("Test overview");
    }

    [Fact]
    public void Blueprint_BackwardsCompatibility_DeserializeWithoutInstructions()
    {
        // Simulates loading a blueprint saved before instructions were added
        var legacyJson = """
        {
            "id": "bp-legacy",
            "title": "Legacy Blueprint",
            "description": "Created before instructions existed",
            "version": 1,
            "participants": [],
            "actions": []
        }
        """;

        var blueprint = JsonSerializer.Deserialize<Models.Blueprint>(legacyJson, JsonOptions);

        blueprint.Should().NotBeNull();
        blueprint!.Id.Should().Be("bp-legacy");
        blueprint.Instructions.Should().BeNull();
    }

    [Fact]
    public void Blueprint_BackwardsCompatibility_DeserializeWithInstructions()
    {
        var json = """
        {
            "id": "bp-new",
            "title": "New Blueprint",
            "description": "Created with instructions",
            "version": 1,
            "instructions": {
                "overview": "## Help\nThis is help text.",
                "locale": "en-GB",
                "actionInstructions": {
                    "0": "Step one instructions"
                },
                "governanceRoles": {
                    "reviewer": "did:sorcha:participant:rev-001"
                }
            },
            "participants": [],
            "actions": []
        }
        """;

        var blueprint = JsonSerializer.Deserialize<Models.Blueprint>(json, JsonOptions);

        blueprint.Should().NotBeNull();
        blueprint!.Instructions.Should().NotBeNull();
        blueprint.Instructions!.Overview.Should().Contain("This is help text.");
        blueprint.Instructions.Locale.Should().Be("en-GB");
        blueprint.Instructions.ActionInstructions.Should().ContainKey(0);
        blueprint.Instructions.GovernanceRoles.Should().ContainKey("reviewer");
    }

    [Fact]
    public void Action_Instructions_NullByDefault()
    {
        var action = new Models.Action();
        action.Instructions.Should().BeNull();
    }

    [Fact]
    public void Action_Instructions_RoundTrip()
    {
        var action = new Models.Action
        {
            Title = "Submit",
            Instructions = "Complete all required fields and attach supporting documentation."
        };

        var json = JsonSerializer.Serialize(action, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Models.Action>(json, JsonOptions);

        deserialized!.Instructions.Should().Be(action.Instructions);
    }

    [Fact]
    public void Action_BackwardsCompatibility_DeserializeWithoutInstructions()
    {
        var legacyJson = """
        {
            "id": 0,
            "title": "Submit",
            "description": "Submit the form",
            "sender": "",
            "isStartingAction": true
        }
        """;

        var action = JsonSerializer.Deserialize<Models.Action>(legacyJson, JsonOptions);

        action.Should().NotBeNull();
        action!.Instructions.Should().BeNull();
        action.Title.Should().Be("Submit");
    }

    [Fact]
    public void Control_Instructions_NullByDefault()
    {
        var control = new Control();
        control.Instructions.Should().BeNull();
    }

    [Fact]
    public void Control_Instructions_RoundTrip()
    {
        var control = new Control
        {
            Title = "Request Type",
            Instructions = "Select the category that best matches your request."
        };

        var json = JsonSerializer.Serialize(control, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Control>(json, JsonOptions);

        deserialized!.Instructions.Should().Be(control.Instructions);
    }

    [Fact]
    public void Participant_Instructions_NullByDefault()
    {
        var participant = new Participant();
        participant.Instructions.Should().BeNull();
    }

    [Fact]
    public void Participant_Instructions_RoundTrip()
    {
        var participant = new Participant
        {
            Name = "Reviewer",
            Instructions = "Check the request against compliance guidelines before approving."
        };

        var json = JsonSerializer.Serialize(participant, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Participant>(json, JsonOptions);

        deserialized!.Instructions.Should().Be(participant.Instructions);
    }
}
