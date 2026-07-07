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
/// Feature 176 US1 — with a real disclosed payload the agent decides on the applicant's actual submitted
/// data: a non-existent postcode drives a rejection, a valid one an approval. Uses the real external-check
/// runner and the real postcode check (offline fixture) over the payload, so the discriminating check is
/// evaluated against the real <c>/address/postcode</c> field, not a default.
/// </summary>
public class DecideOnRealDataTests
{
    private const string ActionName = "Verify Assured Identity Application";

    /// <summary>A check that emits a fixed boolean fact (for the non-discriminating checks).</summary>
    private sealed class FactCheck(string name, bool value) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => Task.FromResult(new ExternalCheckResult(name, value, null));
    }

    private static ActorRule Reject(string condition, string notes) => new()
    {
        ActionName = ActionName,
        Condition = JsonNode.Parse(condition),
        Decision = "reject",
        Payload = JsonNode.Parse($$"""{ "decision": "rejected", "verificationNotes": {{JsonSerializer.Serialize(notes)}} }""")!.AsObject(),
    };

    private static ActorRule[] Rules() =>
    [
        Reject("""{ "==": [ { "var": "checks.postcodeExists" }, false ] }""", "AIAS could not locate that address on any map."),
        Reject("""{ "==": [ { "var": "checks.emailVerified" }, false ] }""", "AIAS needs a verified email before it can assure you."),
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
        // Offline-Always: the postcode check resolves purely against the bundled fixture — no network.
        var postcode = new PostcodeExistsCheck(
            "postcodeExists", "/address", new HttpClient(), ["SW1A 1AA"], PostcodeOfflineMode.Always);
        var runner = new ExternalCheckRunner([postcode, new FactCheck("emailVerified", true)]);
        return new RulesDecisionEngine(Rules(), runner);
    }

    private static PendingAction Application(string postcode) => new()
    {
        ActionId = "a", ActionName = ActionName, ActionIndex = 2,
        BlueprintId = "bp", InstanceId = "i", RegisterId = "r", TransactionId = "t",
        PreviousPayload = JsonSerializer.Deserialize<JsonElement>(
            $$"""{ "name": { "fullName": "Ada Lovelace" }, "address": { "postcode": {{JsonSerializer.Serialize(postcode)}} } }"""),
    };

    [Fact]
    public async Task Decide_RealDisclosedPayload_NonExistentPostcode_Rejects()
    {
        var decision = await Engine().DecideAsync(Application("ZZ99 9ZZ"));

        decision.Decision.Should().Be("reject");
        decision.Payload!["verificationNotes"]!.ToString().Should().Contain("could not locate that address");
    }

    [Fact]
    public async Task Decide_RealDisclosedPayload_ValidPostcode_Approves()
    {
        var decision = await Engine().DecideAsync(Application("SW1A 1AA"));

        decision.Decision.Should().Be("approve");
        decision.Payload!["decision"]!.ToString().Should().Be("approved");
    }
}
