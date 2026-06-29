// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
using Sorcha.Agent.Decision.Checks;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Decision.Checks;

/// <summary>
/// Exercises the extended <see cref="RulesDecisionEngine"/> against the AIAS Assure-ID rule table
/// (data-model.md): checks produce facts, first-match-wins JSON Logic decides. A reusable check
/// emits a configured fact so each scenario drives the rules deterministically.
/// </summary>
public class RulesDecisionEngineChecksTests
{
    private const string ActionName = "Verify Assured Identity Application";

    /// <summary>A check that emits a fixed boolean fact (with optional detail).</summary>
    private sealed class FactCheck(string name, bool value, string? detail = null) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => Task.FromResult(new ExternalCheckResult(name, value, detail));
    }

    private static ActorRule Reject(string condition, string notes) => new()
    {
        ActionName = ActionName,
        Condition = JsonNode.Parse(condition),
        Decision = "reject",
        Payload = JsonNode.Parse($$"""{ "decision": "rejected", "verificationNotes": {{JsonSerializer.Serialize(notes)}} }""")!.AsObject()
    };

    // The AIAS Assure-ID rule table — rejections first, catch-all approve last (first match wins).
    private static ActorRule[] AiasRules() =>
    [
        Reject("""{ "==": [ { "var": "checks.postcodeExists" }, false ] }""", "AIAS could not locate that address on any map."),
        Reject("""{ "==": [ { "var": "checks.profane" }, true ] }""", "AIAS does not assure identities described in such... colourful terms."),
        Reject("""{ "==": [ { "var": "checks.emailVerified" }, false ] }""", "AIAS needs a verified email before it can assure you."),
        new()
        {
            ActionName = ActionName,
            Condition = JsonNode.Parse("""{ "==": [ true, true ] }"""),
            Decision = "approve",
            Payload = JsonNode.Parse("""{ "decision": "approved", "verificationNotes": "Assured by AIAS." }""")!.AsObject()
        }
    ];

    private static RulesDecisionEngine Engine(params IExternalCheck[] checks) =>
        new(AiasRules(), new ExternalCheckRunner(checks));

    private static PendingAction Action() => new()
    {
        ActionId = "act-1",
        ActionName = ActionName,
        ActionIndex = 2,
        BlueprintId = "bp-1",
        InstanceId = "inst-1",
        RegisterId = "reg-1",
        TransactionId = "tx-1",
        PreviousPayload = JsonSerializer.Deserialize<JsonElement>(
            """{ "name": { "fullName": "Alice Smith" }, "address": { "postcode": "SW1A 1AA" } }""")
    };

    [Fact]
    public async Task Decide_CleanApplication_Approves()
    {
        var engine = Engine(
            new FactCheck("postcodeExists", true),
            new FactCheck("profane", false),
            new FactCheck("emailVerified", true));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("approve");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be("Assured by AIAS.");
    }

    [Fact]
    public async Task Decide_NonExistentPostcode_RejectsWithMapReason()
    {
        var engine = Engine(
            new FactCheck("postcodeExists", false),
            new FactCheck("profane", false),
            new FactCheck("emailVerified", true));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("reject");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be("AIAS could not locate that address on any map.");
    }

    [Fact]
    public async Task Decide_ProfaneDetails_RejectsWithProfanityReason()
    {
        var engine = Engine(
            new FactCheck("postcodeExists", true),
            new FactCheck("profane", true),
            new FactCheck("emailVerified", true));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("reject");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be("AIAS does not assure identities described in such... colourful terms.");
    }

    [Fact]
    public async Task Decide_UnverifiedEmail_RejectsWithEmailReason()
    {
        var engine = Engine(
            new FactCheck("postcodeExists", true),
            new FactCheck("profane", false),
            new FactCheck("emailVerified", false));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("reject");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be("AIAS needs a verified email before it can assure you.");
    }

    [Fact]
    public async Task Decide_PostcodeTakesPrecedenceOverProfanity_FirstMatchWins()
    {
        // Both postcode and profanity fail — the rule order means the postcode reason wins.
        var engine = Engine(
            new FactCheck("postcodeExists", false),
            new FactCheck("profane", true),
            new FactCheck("emailVerified", true));

        var decision = await engine.DecideAsync(Action());

        decision.Payload!["verificationNotes"]!.ToString().Should().Be("AIAS could not locate that address on any map.");
    }

    [Fact]
    public async Task Decide_RunnerFaults_HoldsRatherThanApproving()
    {
        // Security regression: when the checks runner itself throws (runner-infrastructure fault,
        // distinct from a per-check fault which ExternalCheckRunner already contains to false),
        // BuildChecksFactsAsync catches and returns null, omitting the "checks" key entirely.
        // JSON Logic resolves {"var":"checks.postcodeExists"} to null; null == false is false
        // under strict equality, so all three reject rules pass and the catch-all approve fires.
        // Fix: fail closed — when checksFacts is null (runner faulted), return "hold".
        var engine = new RulesDecisionEngine(AiasRules(), new ThrowingRunner());

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("hold", "a runner-level fault must not approve; fail closed");
        decision.Payload.Should().BeNull();
        decision.Reasoning.Should().Contain("manual review");
    }

    /// <summary>
    /// Runner stub that throws at the runner level, bypassing per-check fault containment.
    /// Exercises the null-checksFacts path in <see cref="RulesDecisionEngine.DecideAsync"/>.
    /// </summary>
    private sealed class ThrowingRunner : IExternalCheckRunner
    {
        public bool HasChecks => true;
        public Task<IReadOnlyDictionary<string, object?>> RunAsync(
            IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => throw new InvalidOperationException("runner-level fault");
    }
}
