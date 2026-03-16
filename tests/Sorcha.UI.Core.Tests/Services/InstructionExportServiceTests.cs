// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Services;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Tests for InstructionExportService — export/import of blueprint instruction strings.
/// </summary>
public class InstructionExportServiceTests
{
    private readonly InstructionExportService _service = new();

    private static BlueprintModel CreateTestBlueprint()
    {
        return new BlueprintModel
        {
            Id = "bp-1",
            Title = "Test Blueprint",
            Description = "A test blueprint",
            Instructions = new BlueprintInstructions
            {
                Overview = "This is the overview",
                Locale = "en-GB",
                ActionInstructions = new Dictionary<int, string>
                {
                    { 0, "Submit the form" },
                    { 1, "Review the submission" }
                },
                ParticipantInstructions = new Dictionary<string, string>
                {
                    { "Author", "You are the document author" },
                    { "Reviewer", "You are the reviewer" }
                }
            },
            Participants =
            [
                new Participant { Name = "Author" },
                new Participant { Name = "Reviewer" }
            ],
            Actions =
            [
                new ActionModel { Id = 0, Title = "Submit", Instructions = "Fill in the form below" },
                new ActionModel { Id = 1, Title = "Review", Instructions = "Check the submission" }
            ]
        };
    }

    [Fact]
    public void Export_ProducesCorrectKeys()
    {
        var blueprint = CreateTestBlueprint();
        var json = _service.Export(blueprint);
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;

        entries.Should().ContainKey("blueprint.overview");
        entries["blueprint.overview"].Should().Be("This is the overview");

        entries.Should().ContainKey("action.0.instructions");
        entries["action.0.instructions"].Should().Be("Submit the form");

        entries.Should().ContainKey("action.1.instructions");
        entries["action.1.instructions"].Should().Be("Review the submission");

        entries.Should().ContainKey("action.0.inline");
        entries["action.0.inline"].Should().Be("Fill in the form below");

        entries.Should().ContainKey("participant.Author.instructions");
        entries["participant.Author.instructions"].Should().Be("You are the document author");
    }

    [Fact]
    public void Import_PrimaryLocale_UpdatesInlineText()
    {
        var blueprint = CreateTestBlueprint();
        var importJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "blueprint.overview", "Updated overview" },
            { "action.0.instructions", "Updated action instructions" },
            { "action.0.inline", "Updated inline instructions" },
            { "participant.Author.instructions", "Updated author guidance" }
        });

        var result = _service.Import(blueprint, importJson);

        result.Instructions!.Overview.Should().Be("Updated overview");
        result.Instructions.ActionInstructions![0].Should().Be("Updated action instructions");
        result.Actions[0].Instructions.Should().Be("Updated inline instructions");
        result.Instructions.ParticipantInstructions!["Author"].Should().Be("Updated author guidance");
    }

    [Fact]
    public void Import_PrimaryLocale_NullLocale_UpdatesInlineText()
    {
        var blueprint = CreateTestBlueprint();
        var importJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "blueprint.overview", "New overview" }
        });

        var result = _service.Import(blueprint, importJson, locale: null);
        result.Instructions!.Overview.Should().Be("New overview");
    }

    [Fact]
    public void Import_ForeignLocale_CreatesInstructionSet()
    {
        var blueprint = CreateTestBlueprint();
        var importJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "blueprint.overview", "Ceci est la vue d'ensemble" },
            { "action.0.instructions", "Soumettre le formulaire" }
        });

        var result = _service.Import(blueprint, importJson, locale: "fr-FR");

        result.Instructions!.InstructionSets.Should().HaveCount(1);
        result.Instructions.InstructionSets![0].Locale.Should().Be("fr-FR");
        result.Instructions.InstructionSets[0].Source.Should().StartWith("data:application/json;base64,");

        // Primary text should NOT be changed
        result.Instructions.Overview.Should().Be("This is the overview");
    }

    [Fact]
    public void Import_ForeignLocale_UpdatesExistingInstructionSet()
    {
        var blueprint = CreateTestBlueprint();
        blueprint.Instructions!.InstructionSets =
        [
            new InstructionSet { Locale = "fr-FR", Source = "old-source", Version = "2025-01-01" }
        ];

        var importJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            { "blueprint.overview", "Nouvelle vue d'ensemble" }
        });

        var result = _service.Import(blueprint, importJson, locale: "fr-FR");

        result.Instructions!.InstructionSets.Should().HaveCount(1);
        result.Instructions.InstructionSets![0].Source.Should().StartWith("data:application/json;base64,");
        result.Instructions.InstructionSets[0].Source.Should().NotBe("old-source");
    }

    [Fact]
    public void RoundTrip_ExportImport_PreservesContent()
    {
        var blueprint = CreateTestBlueprint();
        var exported = _service.Export(blueprint);

        // Create a fresh blueprint and import
        var fresh = new BlueprintModel
        {
            Id = "bp-2",
            Title = "Fresh",
            Description = "Fresh blueprint",
            Actions =
            [
                new ActionModel { Id = 0, Title = "Submit" },
                new ActionModel { Id = 1, Title = "Review" }
            ],
            Participants =
            [
                new Participant { Name = "Author" },
                new Participant { Name = "Reviewer" }
            ]
        };

        var result = _service.Import(fresh, exported);

        result.Instructions!.Overview.Should().Be("This is the overview");
        result.Instructions.ActionInstructions![0].Should().Be("Submit the form");
        result.Instructions.ActionInstructions[1].Should().Be("Review the submission");
        result.Actions[0].Instructions.Should().Be("Fill in the form below");
        result.Instructions.ParticipantInstructions!["Author"].Should().Be("You are the document author");
    }

    [Fact]
    public void Export_NullBlueprint_Throws()
    {
        var act = () => _service.Export(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Export_NoInstructions_ReturnsEmptyObject()
    {
        var blueprint = new BlueprintModel
        {
            Id = "bp-empty",
            Title = "Empty",
            Description = "No instructions",
            Participants = [new Participant { Name = "A" }, new Participant { Name = "B" }],
            Actions = [new ActionModel { Id = 0, Title = "Step" }]
        };

        var json = _service.Export(blueprint);
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        entries.Should().BeEmpty();
    }
}
