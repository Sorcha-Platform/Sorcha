// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Forms;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// T051 [US5] — verify the three new chat tools (<c>set_form_layout</c>, <c>set_field_autofill</c>,
/// <c>set_review_page</c>) write the same JSON the direct-manipulation path (UI's
/// <c>FormLayoutAuthoringService</c> via the shared <see cref="FormLayoutWriter"/>) produces.
/// FR-016 — direct manipulation and chat converge on the same form definition.
/// </summary>
public class FormLayoutChatToolsTests
{
    private readonly BlueprintToolExecutor _executor;

    public FormLayoutChatToolsTests()
    {
        _executor = new BlueprintToolExecutor(
            new Mock<ILogger<BlueprintToolExecutor>>().Object,
            new Mock<ISchemaIndexService>().Object,
            new Mock<IBlueprintTemplateService>().Object);
    }

    private static BlueprintBuilder MakeBuilder()
    {
        var builder = BlueprintBuilder.Create()
            .WithTitle("F142 US5")
            .WithDescription("converge test")
            .AddParticipant("applicant", p => p.Named("Applicant"))
            .AddAction(1, a => a.WithTitle("Apply").SentBy("applicant"));
        var draft = builder.BuildDraft();
        draft.Actions[0].DataSchemas = new[]
        {
            JsonDocument.Parse(
                """{"type":"object","properties":{"name":{"type":"string"},"email":{"type":"string"}}}"""),
        };
        return builder;
    }

    [Fact]
    public async Task SetFormLayout_BulkApply_WritesXPagesAndXIntroduction()
    {
        var builder = MakeBuilder();
        var args = JsonDocument.Parse(
            """
            {
                "actionId":1,
                "pages":[ {"title":"Step 1"}, {"title":"Step 2"} ],
                "introduction":"Please answer carefully."
            }
            """);

        var result = await _executor.ExecuteAsync("set_form_layout", args, builder);

        result.Success.Should().BeTrue();
        var schema = builder.BuildDraft().Actions[0].DataSchemas!.First().RootElement;
        schema.GetProperty("x-pages").GetArrayLength().Should().Be(2);
        schema.GetProperty("x-introduction").GetString().Should().Be("Please answer carefully.");
    }

    [Fact]
    public async Task SetFieldAutofill_WithKey_WritesXPersona()
    {
        var builder = MakeBuilder();
        var args = JsonDocument.Parse(
            """{ "actionId":1, "fieldPath":"/email", "personaKey":"email" }""");

        var result = await _executor.ExecuteAsync("set_field_autofill", args, builder);

        result.Success.Should().BeTrue();
        var email = builder.BuildDraft().Actions[0].DataSchemas!.First()
            .RootElement.GetProperty("properties").GetProperty("email");
        email.GetProperty("x-persona").GetString().Should().Be("email");
    }

    [Fact]
    public async Task SetFieldAutofill_NullKey_ClearsXPersona()
    {
        var builder = MakeBuilder();
        await _executor.ExecuteAsync("set_field_autofill",
            JsonDocument.Parse("""{ "actionId":1, "fieldPath":"/email", "personaKey":"email" }"""),
            builder);

        var clearResult = await _executor.ExecuteAsync("set_field_autofill",
            JsonDocument.Parse("""{ "actionId":1, "fieldPath":"/email" }"""),
            builder);

        clearResult.Success.Should().BeTrue();
        var email = builder.BuildDraft().Actions[0].DataSchemas!.First()
            .RootElement.GetProperty("properties").GetProperty("email");
        email.TryGetProperty("x-persona", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SetReviewPage_MarksTargetPage()
    {
        var builder = MakeBuilder();
        await _executor.ExecuteAsync("set_form_layout",
            JsonDocument.Parse("""{ "actionId":1, "pages":[ {"title":"A"}, {"title":"B"} ] }"""),
            builder);

        var result = await _executor.ExecuteAsync("set_review_page",
            JsonDocument.Parse("""{ "actionId":1, "pageIndex":1 }"""),
            builder);

        result.Success.Should().BeTrue();
        var pages = builder.BuildDraft().Actions[0].DataSchemas!.First()
            .RootElement.GetProperty("x-pages");
        pages[0].TryGetProperty("x-review", out _).Should().BeFalse();
        pages[1].TryGetProperty("x-review", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ChatToolAndDirectWriter_ProduceByteEquivalentSchemaJson()
    {
        // FR-016: chat (set_form_layout) and direct manipulation (FormLayoutWriter)
        // must produce byte-identical schema JSON for the same inputs.
        var chatBuilder = MakeBuilder();
        var chatArgs = JsonDocument.Parse(
            """
            {
                "actionId":1,
                "pages":[ {"title":"Step 1"}, {"title":"Step 2"} ],
                "introduction":"Hi.",
                "widths":{ "email":"half" }
            }
            """);
        await _executor.ExecuteAsync("set_form_layout", chatArgs, chatBuilder);
        var chatSchemaJson = chatBuilder.BuildDraft().Actions[0].DataSchemas!.First()
            .RootElement.GetRawText();

        // Direct path: same writer, same inputs.
        var startingSchema = JsonDocument.Parse(
            """{"type":"object","properties":{"name":{"type":"string"},"email":{"type":"string"}}}""");
        var pages = System.Text.Json.Nodes.JsonNode.Parse(
            """[ {"title":"Step 1"}, {"title":"Step 2"} ]""") as System.Text.Json.Nodes.JsonArray;
        var direct = FormLayoutWriter.SetFormLayout(
            startingSchema,
            pages: pages,
            introduction: "Hi.",
            widths: new Dictionary<string, string> { ["email"] = "half" });
        var directJson = direct.RootElement.GetRawText();

        chatSchemaJson.Should().Be(directJson, "chat tool MUST be byte-equivalent to the direct writer");
    }

    [Fact]
    public async Task SetFormLayout_OnUnknownAction_Fails()
    {
        var builder = MakeBuilder();
        var result = await _executor.ExecuteAsync(
            "set_form_layout",
            JsonDocument.Parse("""{ "actionId":99, "introduction":"x" }"""),
            builder);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("99");
    }
}
