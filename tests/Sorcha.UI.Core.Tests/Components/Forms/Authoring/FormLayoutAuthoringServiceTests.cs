// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.Blueprint.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Components.Forms.Authoring;

/// <summary>
/// T047 [US5] — verify <see cref="FormLayoutAuthoringService"/> writes the same JSON the
/// shared <see cref="FormLayoutWriter"/> does (byte-equivalence with the chat-tool path),
/// and that it operates on <c>Action.DataSchemas[0]</c> in place.
/// </summary>
public class FormLayoutAuthoringServiceTests
{
    private readonly FormLayoutAuthoringService _sut = new();

    private static BlueprintAction MakeAction()
    {
        var schema = JsonDocument.Parse(
            """
            {
                "type":"object",
                "properties":{
                    "email":{"type":"string","format":"email"},
                    "name":{"type":"string"}
                }
            }
            """);
        return new BlueprintAction
        {
            Id = 1,
            Title = "Apply",
            DataSchemas = new List<JsonDocument> { schema },
        };
    }

    [Fact]
    public void SetPages_OnService_ProducesSameJsonAsDirectWriterCall()
    {
        var action = MakeAction();
        var pages = new JsonArray
        {
            new JsonObject { ["title"] = "A" },
            new JsonObject { ["title"] = "B" },
        };

        var result = _sut.SetPages(action, pages);
        var resultJson = result.DataSchemas!.First().RootElement.GetRawText();

        // Reference comparison: call the writer directly.
        var direct = FormLayoutWriter.SetPages(MakeAction().DataSchemas!.First(), pages);
        var directJson = direct.RootElement.GetRawText();

        resultJson.Should().Be(directJson, "the service must call the same shared writer used by the chat tool path");
    }

    [Fact]
    public void SetFieldPersona_OnService_WritesXPersonaOntoFirstSchema()
    {
        var action = MakeAction();

        _sut.SetFieldPersona(action, "/email", "email");

        var root = action.DataSchemas!.First().RootElement;
        root.GetProperty("properties").GetProperty("email")
            .GetProperty("x-persona").GetString().Should().Be("email");
    }

    [Fact]
    public void SetSections_OnService_WritesXSections()
    {
        var action = MakeAction();
        var sections = new JsonArray
        {
            new JsonObject { ["title"] = "Personal", ["fields"] = new JsonArray { "name", "email" } },
        };

        _sut.SetSections(action, sections);

        var root = action.DataSchemas!.First().RootElement;
        root.GetProperty("x-sections").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void SetIntroduction_OnService_WritesXIntroduction()
    {
        var action = MakeAction();

        _sut.SetIntroduction(action, "Read me first.");

        var root = action.DataSchemas!.First().RootElement;
        root.GetProperty("x-introduction").GetString().Should().Be("Read me first.");
    }

    [Fact]
    public void EveryServiceWrite_PreservesPresentationalClassification()
    {
        var action = MakeAction();

        _sut.SetSections(action, new JsonArray { new JsonObject { ["title"] = "S", ["fields"] = new JsonArray { "name" } } });
        _sut.SetPages(action, new JsonArray { new JsonObject { ["title"] = "P" } });
        _sut.SetFieldWidth(action, "/email", "half");
        _sut.SetFieldPersona(action, "/email", "email");
        _sut.SetIntroduction(action, "intro");
        _sut.SetAddressLookup(action, "/name", true);

        var root = action.DataSchemas!.First().RootElement;
        var topLevelKeys = root.EnumerateObject().Select(p => p.Name)
            .Where(n => n.StartsWith("x-"))
            .ToArray();
        foreach (var k in topLevelKeys)
        {
            FormKeywordClassifier.IsPresentational(k).Should().BeTrue($"{k} must be presentational");
        }
    }
}
