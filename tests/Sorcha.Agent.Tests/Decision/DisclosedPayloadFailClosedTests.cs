// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
using Sorcha.Agent.Decision.Checks;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Decision;

/// <summary>
/// Feature 176 US2 — the <see cref="RulesDecisionEngine"/> holds (never approves/rejects) when the rules
/// depend on the disclosed application payload but it is empty/unavailable, and decides correctly once the
/// payload is present. This is the fail-closed guard that stops the AIAS blank-data defect (a missing
/// payload made every check default and the catch-all approve fire).
/// </summary>
public class DisclosedPayloadFailClosedTests
{
    private const string ActionName = "Verify Assured Identity Application";

    private static ActorRule[] Rules() =>
    [
        new()
        {
            ActionName = ActionName,
            Condition = JsonNode.Parse("""{ "==": [ { "var": "checks.postcodeExists" }, false ] }"""),
            Decision = "reject",
            Payload = JsonNode.Parse("""{ "decision": "rejected", "verificationNotes": "AIAS could not locate that address on any map." }""")!.AsObject(),
        },
        new()
        {
            ActionName = ActionName,
            Condition = JsonNode.Parse("""{ "==": [ true, true ] }"""),
            Decision = "approve",
            Payload = JsonNode.Parse("""{ "decision": "approved", "verificationNotes": "Assured by AIAS." }""")!.AsObject(),
        },
    ];

    private static RulesDecisionEngine Engine()
    {
        var postcode = new PostcodeExistsCheck(
            "postcodeExists", "/address", new HttpClient(), ["SW1A 1AA"], PostcodeOfflineMode.Always);
        return new RulesDecisionEngine(Rules(), new ExternalCheckRunner([postcode]));
    }

    private static PendingAction Action(JsonElement? payload) => new()
    {
        ActionId = "a", ActionName = ActionName, ActionIndex = 2,
        BlueprintId = "bp", InstanceId = "i", RegisterId = "r", TransactionId = "t",
        PreviousPayload = payload,
    };

    [Fact]
    public async Task Decide_RulesRequireChecks_NullPayload_HoldsFailClosed()
    {
        var decision = await Engine().DecideAsync(Action(null));

        decision.Decision.Should().Be("hold", "rules requiring the disclosed payload must not decide on a blank view");
        decision.Payload.Should().BeNull();
        decision.Reasoning.Should().Contain("Disclosed application data unavailable");
    }

    [Fact]
    public async Task Decide_RulesRequireChecks_EmptyObjectPayload_HoldsFailClosed()
    {
        var decision = await Engine().DecideAsync(Action(JsonSerializer.Deserialize<JsonElement>("{}")));

        decision.Decision.Should().Be("hold");
        decision.Payload.Should().BeNull();
    }

    [Fact]
    public async Task Decide_PayloadPresent_DecidesCorrectly_NotHeld()
    {
        var decision = await Engine().DecideAsync(Action(
            JsonSerializer.Deserialize<JsonElement>("""{ "address": { "postcode": "SW1A 1AA" } }""")));

        decision.Decision.Should().Be("approve", "with the payload present the checks evaluate and the rules decide");
    }
}
