// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Decision;

public class AiDecisionEngineTests
{
    private static PendingAction CreateAction() => new()
    {
        ActionId = "act-1",
        ActionName = "ReviewApplication",
        ActionIndex = 3,
        BlueprintId = "bp-1",
        InstanceId = "inst-1",
        RegisterId = "reg-1",
        TransactionId = "tx-1",
        PreviousPayload = JsonSerializer.Deserialize<JsonElement>("{\"projectName\": \"Riverside Heights\"}"),
        Schema = JsonSerializer.Deserialize<JsonElement>("""
            {
              "type": "object",
              "properties": {
                "zoningCompliant": { "type": "boolean" },
                "planningNotes": { "type": "string" }
              },
              "required": ["zoningCompliant", "planningNotes"]
            }
            """)
    };

    [Fact]
    public void AiDecisionEngine_CanBeConstructed()
    {
        var config = new AiConfig
        {
            PromptFile = "test-prompt.md",
            Model = "claude-sonnet-4-6",
            Temperature = 0.3
        };
        var logger = new Mock<ILogger<AiDecisionEngine>>();
        var httpClient = new HttpClient();

        var engine = new AiDecisionEngine(config, httpClient, logger.Object);
        engine.Should().NotBeNull();
    }

    [Fact]
    public void BuildPrompt_IncludesActionContext()
    {
        var action = CreateAction();
        var prompt = AiDecisionEngine.BuildContextMessage(action, "You are a planning officer.");

        prompt.Should().Contain("ReviewApplication");
        prompt.Should().Contain("zoningCompliant");
        prompt.Should().Contain("Riverside Heights");
        prompt.Should().Contain("planning officer");
    }

    [Fact]
    public void ParsePayload_ValidJson_ReturnsPayload()
    {
        var json = """{"zoningCompliant": true, "planningNotes": "Approved"}""";
        var result = AiDecisionEngine.TryParsePayload(json);

        result.Should().NotBeNull();
        result.Should().ContainKey("zoningCompliant");
        result.Should().ContainKey("planningNotes");
    }

    [Fact]
    public void ParsePayload_InvalidJson_ReturnsNull()
    {
        var result = AiDecisionEngine.TryParsePayload("not json at all");
        result.Should().BeNull();
    }

    [Fact]
    public void ParsePayload_ExtractsJsonFromMarkdown()
    {
        var markdown = """
            Here is my response:
            ```json
            {"zoningCompliant": true, "planningNotes": "Looks good"}
            ```
            """;
        var result = AiDecisionEngine.TryParsePayload(markdown);
        result.Should().NotBeNull();
        result.Should().ContainKey("zoningCompliant");
    }
}
