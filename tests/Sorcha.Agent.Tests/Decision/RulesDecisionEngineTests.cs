// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Decision;

public class RulesDecisionEngineTests
{
    private static PendingAction CreateAction(string actionName = "ReviewApplication", string? payloadJson = null) => new()
    {
        ActionId = "act-1",
        ActionName = actionName,
        ActionIndex = 1,
        BlueprintId = "bp-1",
        InstanceId = "inst-1",
        RegisterId = "reg-1",
        TransactionId = "tx-1",
        PreviousPayload = payloadJson is not null
            ? JsonSerializer.Deserialize<JsonElement>(payloadJson)
            : null
    };

    private static RulesDecisionEngine CreateEngine(params ActorRule[] rules) => new(rules);

    [Fact]
    public async Task DecideAsync_SingleMatchingRule_ReturnsApprove()
    {
        var rules = new[]
        {
            new ActorRule
            {
                ActionName = "ReviewApplication",
                Decision = "approve",
                Payload = JsonNode.Parse("{\"decision\": \"approved\"}")!.AsObject()
            }
        };
        var engine = CreateEngine(rules);

        var result = await engine.DecideAsync(CreateAction());

        result.Decision.Should().Be("approve");
        result.Payload.Should().ContainKey("decision");
    }

    [Fact]
    public async Task DecideAsync_NoMatchingAction_ReturnsSkip()
    {
        var rules = new[]
        {
            new ActorRule
            {
                ActionName = "SubmitApplication",
                Decision = "approve",
                Payload = JsonNode.Parse("{}")!.AsObject()
            }
        };
        var engine = CreateEngine(rules);

        var result = await engine.DecideAsync(CreateAction("ReviewApplication"));

        result.Decision.Should().Be("skip");
    }

    [Fact]
    public async Task DecideAsync_FirstMatchWins_ReturnsFirstMatchingRule()
    {
        var rules = new[]
        {
            new ActorRule
            {
                ActionName = "ReviewApplication",
                Decision = "reject",
                Payload = JsonNode.Parse("{\"reason\": \"rejected\"}")!.AsObject()
            },
            new ActorRule
            {
                ActionName = "ReviewApplication",
                Decision = "approve",
                Payload = JsonNode.Parse("{\"reason\": \"approved\"}")!.AsObject()
            }
        };
        var engine = CreateEngine(rules);

        var result = await engine.DecideAsync(CreateAction());

        result.Decision.Should().Be("reject");
    }

    [Fact]
    public async Task DecideAsync_ConditionEvaluation_MatchesOnCondition()
    {
        var rules = new[]
        {
            new ActorRule
            {
                ActionName = "ReviewApplication",
                Condition = JsonNode.Parse("{\">\": [{\"var\": \"payload.estimatedCost\"}, 500000]}"),
                Decision = "reject",
                Payload = JsonNode.Parse("{\"reason\": \"too expensive\"}")!.AsObject()
            },
            new ActorRule
            {
                ActionName = "ReviewApplication",
                Decision = "approve",
                Payload = JsonNode.Parse("{\"reason\": \"approved\"}")!.AsObject()
            }
        };
        var engine = CreateEngine(rules);

        // Cost above threshold — should hit reject rule
        var highCostResult = await engine.DecideAsync(
            CreateAction(payloadJson: "{\"estimatedCost\": 600000}"));
        highCostResult.Decision.Should().Be("reject");

        // Cost below threshold — skip first rule, hit catch-all approve
        var lowCostResult = await engine.DecideAsync(
            CreateAction(payloadJson: "{\"estimatedCost\": 100000}"));
        lowCostResult.Decision.Should().Be("approve");
    }

    [Fact]
    public async Task DecideAsync_SkipDecision_ReturnsSkip()
    {
        var rules = new[]
        {
            new ActorRule
            {
                ActionName = "ReviewApplication",
                Decision = "skip"
            }
        };
        var engine = CreateEngine(rules);

        var result = await engine.DecideAsync(CreateAction());

        result.Decision.Should().Be("skip");
        result.Payload.Should().BeNull();
    }
}
