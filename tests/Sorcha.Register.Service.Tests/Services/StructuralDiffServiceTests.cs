// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class StructuralDiffServiceTests
{
    private readonly StructuralDiffService _service = new();

    private const string BaseBlueprintJson = """
    {
        "id": "bp-001",
        "title": "Approval Workflow",
        "description": "A test workflow",
        "version": 1,
        "versionMajor": 1,
        "versionMinor": 0,
        "participants": [
            { "id": "p1", "name": "Applicant", "walletAddress": "addr1" },
            { "id": "p2", "name": "Reviewer", "walletAddress": "addr2" }
        ],
        "actions": [
            {
                "id": 0,
                "title": "Submit",
                "sender": "Applicant",
                "isStartingAction": true,
                "form": {
                    "type": "Layout",
                    "elements": [
                        { "type": "TextLine", "scope": "/name", "title": "Name" }
                    ]
                }
            }
        ]
    }
    """;

    [Fact]
    public void ComputeStructuralHash_IdenticalBlueprints_ProduceSameHash()
    {
        var hash1 = _service.ComputeStructuralHash(BaseBlueprintJson);
        var hash2 = _service.ComputeStructuralHash(BaseBlueprintJson);

        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeStructuralHash_InstructionOnlyChange_ProducesSameHash()
    {
        var withInstructions = """
        {
            "id": "bp-001",
            "title": "Approval Workflow",
            "description": "A test workflow",
            "version": 1,
            "versionMajor": 1,
            "versionMinor": 1,
            "instructions": {
                "overview": "## Help\nThis is a workflow for approvals.",
                "locale": "en-GB",
                "actionInstructions": { "0": "Fill in the form" },
                "participantInstructions": { "Applicant": "You submit the request" }
            },
            "participants": [
                { "id": "p1", "name": "Applicant", "walletAddress": "addr1" },
                { "id": "p2", "name": "Reviewer", "walletAddress": "addr2" }
            ],
            "actions": [
                {
                    "id": 0,
                    "title": "Submit",
                    "sender": "Applicant",
                    "instructions": "Complete all required fields.",
                    "isStartingAction": true,
                    "form": {
                        "type": "Layout",
                        "elements": [
                            { "type": "TextLine", "scope": "/name", "title": "Name", "instructions": "Enter your full name" }
                        ]
                    }
                }
            ]
        }
        """;

        var hashBase = _service.ComputeStructuralHash(BaseBlueprintJson);
        var hashWithInstructions = _service.ComputeStructuralHash(withInstructions);

        hashBase.Should().Be(hashWithInstructions,
            "instruction-only changes should produce the same structural hash");
    }

    [Fact]
    public void ComputeStructuralHash_StructuralChange_ProducesDifferentHash()
    {
        var withNewAction = """
        {
            "id": "bp-001",
            "title": "Approval Workflow",
            "description": "A test workflow",
            "version": 1,
            "versionMajor": 2,
            "versionMinor": 0,
            "participants": [
                { "id": "p1", "name": "Applicant", "walletAddress": "addr1" },
                { "id": "p2", "name": "Reviewer", "walletAddress": "addr2" }
            ],
            "actions": [
                {
                    "id": 0,
                    "title": "Submit",
                    "sender": "Applicant",
                    "isStartingAction": true,
                    "form": {
                        "type": "Layout",
                        "elements": [
                            { "type": "TextLine", "scope": "/name", "title": "Name" }
                        ]
                    }
                },
                {
                    "id": 1,
                    "title": "Review",
                    "sender": "Reviewer",
                    "isStartingAction": false
                }
            ]
        }
        """;

        var hashBase = _service.ComputeStructuralHash(BaseBlueprintJson);
        var hashNew = _service.ComputeStructuralHash(withNewAction);

        hashBase.Should().NotBe(hashNew,
            "adding an action is a structural change");
    }

    [Fact]
    public void ComputeStructuralHash_ParticipantInstructionChange_IsSameHash()
    {
        var withParticipantInstructions = """
        {
            "id": "bp-001",
            "title": "Approval Workflow",
            "description": "A test workflow",
            "version": 1,
            "versionMajor": 1,
            "versionMinor": 0,
            "participants": [
                { "id": "p1", "name": "Applicant", "walletAddress": "addr1", "instructions": "You are the applicant." },
                { "id": "p2", "name": "Reviewer", "walletAddress": "addr2", "instructions": "You review submissions." }
            ],
            "actions": [
                {
                    "id": 0,
                    "title": "Submit",
                    "sender": "Applicant",
                    "isStartingAction": true,
                    "form": {
                        "type": "Layout",
                        "elements": [
                            { "type": "TextLine", "scope": "/name", "title": "Name" }
                        ]
                    }
                }
            ]
        }
        """;

        var hashBase = _service.ComputeStructuralHash(BaseBlueprintJson);
        var hashWithInstructions = _service.ComputeStructuralHash(withParticipantInstructions);

        hashBase.Should().Be(hashWithInstructions);
    }

    [Fact]
    public void ComputeStructuralHash_TitleChange_ProducesDifferentHash()
    {
        var differentTitle = BaseBlueprintJson.Replace("Approval Workflow", "Modified Workflow");

        var hashBase = _service.ComputeStructuralHash(BaseBlueprintJson);
        var hashDifferent = _service.ComputeStructuralHash(differentTitle);

        hashBase.Should().NotBe(hashDifferent,
            "title is a structural field");
    }

    [Fact]
    public void ComputeStructuralHash_JsonElement_ProducesSameHashAsString()
    {
        using var doc = JsonDocument.Parse(BaseBlueprintJson);
        var hashFromElement = _service.ComputeStructuralHash(doc.RootElement);
        var hashFromString = _service.ComputeStructuralHash(BaseBlueprintJson);

        hashFromElement.Should().Be(hashFromString);
    }

    [Fact]
    public void ClassifyChange_SameHash_ReturnsDocumentation()
    {
        var result = StructuralDiffService.ClassifyChange("abc123", "abc123");
        result.Should().Be("documentation");
    }

    [Fact]
    public void ClassifyChange_DifferentHash_ReturnsStructural()
    {
        var result = StructuralDiffService.ClassifyChange("abc123", "def456");
        result.Should().Be("structural");
    }

    [Fact]
    public void ClassifyChange_CaseInsensitive()
    {
        var result = StructuralDiffService.ClassifyChange("ABC123", "abc123");
        result.Should().Be("documentation");
    }

    [Fact]
    public void ComputeStructuralHash_ReturnsLowercaseHex()
    {
        var hash = _service.ComputeStructuralHash(BaseBlueprintJson);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
