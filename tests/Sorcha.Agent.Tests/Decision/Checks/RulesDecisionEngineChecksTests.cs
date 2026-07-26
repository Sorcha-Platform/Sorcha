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

    [Fact]
    public async Task Decide_RulesRequireChecks_RunnerAbsent_HoldsFailClosed()
    {
        // #1077 — the exact live bug: no check runner is wired (e.g. a stale agent build predating the
        // external-check hook), yet the rules reference checks.*. Previously the checks block was
        // skipped entirely, the reject rules no-op'd (null != false), and the catch-all approve fired —
        // issuing a credential with zero verification (fake postcode ZZ99 9ZZ approved). Must hold.
        var engine = new RulesDecisionEngine(AiasRules()); // no runner at all

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("hold", "rules requiring checks must fail closed when no runner is wired");
        decision.Payload.Should().BeNull();
        decision.Reasoning.Should().Contain("manual review");
    }

    [Fact]
    public async Task Decide_RulesRequireChecks_RunnerHasNoChecks_HoldsFailClosed()
    {
        // Runner present but empty (HasChecks == false — e.g. an unresolved/empty ChecksFile). Same
        // absence hole as a null runner: checksFacts stays null while rules reference checks.*.
        var engine = new RulesDecisionEngine(AiasRules(), new ExternalCheckRunner([]));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("hold", "an empty runner (HasChecks false) must also fail closed");
        decision.Payload.Should().BeNull();
    }

    [Fact]
    public async Task Decide_RulesDoNotReferenceChecks_RunnerAbsent_ApprovesNormally_NoForcedHold()
    {
        // Regression guard: an agent whose rules do NOT reference checks.* must be unaffected — no
        // runner, no forced hold. The fail-closed guard is scoped to rules that actually depend on checks.
        var noCheckRules = new[]
        {
            new ActorRule
            {
                ActionName = ActionName,
                Condition = JsonNode.Parse("""{ "==": [ true, true ] }"""),
                Decision = "approve",
                Payload = JsonNode.Parse("""{ "decision": "approved", "verificationNotes": "No checks needed." }""")!.AsObject(),
            },
        };
        var engine = new RulesDecisionEngine(noCheckRules); // no runner, no checks references

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("approve", "agents without checks.* rules must not be force-held");
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

    // --- Numeric check facts through the actual rules pipeline (review finding #1) ---------------
    //
    // Task 4 widened ExternalCheckResult with an optional Numeric, and ExternalCheckRunner merges it
    // as a fact. But RulesDecisionEngineChecksTests / ExternalCheckRunnerTests only ever asserted
    // against the runner's raw IReadOnlyDictionary<string, object?> — never against the JsonObject
    // that BuildChecksFactsAsync actually hands to Json.Logic. That JsonObject-building switch has no
    // numeric arm, so a boxed double falls into the catch-all `JsonValue.Create(value.ToString())` and
    // is stringified ("18" instead of 18). Every numeric band comparison then silently misbehaves.
    // These tests drive a real numeric check through DecideAsync end-to-end so a regression here fails
    // the way the AIAS scoring check (Task 5+) would actually fail in production.

    /// <summary>A check that emits a fixed numeric fact (score-style: Value=true alongside Numeric,
    /// mirroring how a future questionnaire-scoring check is expected to report — see Finding 2).</summary>
    private sealed class NumericFactCheck(string name, double numeric) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => Task.FromResult(new ExternalCheckResult(name, true, null, numeric));
    }

    /// <summary>A check that always throws — exercises SafeEvaluateAsync's fail-closed containment
    /// for a numeric-style check (Finding: must still land in the lowest band, not approve).</summary>
    private sealed class ThrowingCheck(string name) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => throw new InvalidOperationException("scorer boom");
    }

    // A three-band rule table over a numeric "cyberScore" fact (out of 24, pass threshold 12, with a
    // "perfect score" fast-track tier at the maximum) — the shape Task 5's questionnaire-scoring check
    // will actually produce. NOTE on rule choice: Json.Logic's "<" / ">=" already coerce a JSON *string*
    // number back to a double (loose, JS-style comparison), so a plain below/above-threshold pair does
    // NOT reproduce the bug — verified empirically against the pre-fix code before writing this. What
    // DOES reveal the stringification is any comparison that is type-sensitive rather than value-
    // coercing: strict equality ("===") and "in" both return false when comparing a JsonValueKind.String
    // against a JSON number, even for the identical numeric value. The perfect-score tier below uses
    // "===" for exactly that reason — it is the rule shape that actually distinguishes "arrived as a
    // real JSON number" from "arrived as a stringified number".
    private static ActorRule[] CyberScoreRules() =>
    [
        new()
        {
            ActionName = ActionName,
            Condition = JsonNode.Parse("""{ "===": [ { "var": "checks.cyberScore" }, 24 ] }"""),
            Decision = "approve",
            Payload = JsonNode.Parse(
                """{ "decision": "approved", "verificationNotes": "Perfect cyber score achieved." }""")!.AsObject()
        },
        new()
        {
            ActionName = ActionName,
            Condition = JsonNode.Parse("""{ "<": [ { "var": "checks.cyberScore" }, 12 ] }"""),
            Decision = "reject",
            Payload = JsonNode.Parse(
                """{ "decision": "rejected", "verificationNotes": "Cyber score below threshold." }""")!.AsObject()
        },
        new()
        {
            ActionName = ActionName,
            Condition = JsonNode.Parse("""{ ">=": [ { "var": "checks.cyberScore" }, 12 ] }"""),
            Decision = "approve",
            Payload = JsonNode.Parse(
                """{ "decision": "approved", "verificationNotes": "Cyber score meets threshold." }""")!.AsObject()
        }
    ];

    [Fact]
    public async Task Decide_NumericScoreBelowThreshold_Rejects()
    {
        var engine = new RulesDecisionEngine(CyberScoreRules(), new ExternalCheckRunner([new NumericFactCheck("cyberScore", 6)]));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("reject");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be("Cyber score below threshold.");
    }

    [Fact]
    public async Task Decide_NumericScoreAboveThreshold_ApprovesViaGenericBand()
    {
        var engine = new RulesDecisionEngine(CyberScoreRules(), new ExternalCheckRunner([new NumericFactCheck("cyberScore", 18)]));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("approve");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be("Cyber score meets threshold.");
    }

    [Fact]
    public async Task Decide_PerfectNumericScore_MatchesExactTierRule_NotGenericApproveBand()
    {
        // The bug-revealing case: a strict-equality rule ("===": [checks.cyberScore, 24]) must match
        // when the true numeric fact equals 24 exactly. Pre-fix, BuildChecksFactsAsync stringifies the
        // fact to "24" (JsonValueKind.String); "24" === 24 is false under Json.Logic's strict equality
        // (types differ), so this exact-tier rule silently never fires and the generic ">=12" band wins
        // instead — same top-level "approve" outcome, but the WRONG rule, proven by verificationNotes.
        var engine = new RulesDecisionEngine(CyberScoreRules(), new ExternalCheckRunner([new NumericFactCheck("cyberScore", 24)]));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("approve");
        decision.Payload!["verificationNotes"]!.ToString().Should().Be(
            "Perfect cyber score achieved.",
            "the exact-tier ('===') rule must see a real JSON number, not a stringified fact, to match");
    }

    [Fact]
    public async Task Decide_NumericCheckFaults_CoercesToLowestBand_FailClosed()
    {
        // SafeEvaluateAsync contains the fault to ExternalCheckResult(name, false) — Numeric is null,
        // so the boolean false is merged, which Json.Logic coerces to 0, landing in the reject band.
        // Must NOT change: a broken scorer issues no credential.
        var engine = new RulesDecisionEngine(CyberScoreRules(), new ExternalCheckRunner([new ThrowingCheck("cyberScore")]));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("reject", "a faulting scorer must land in the lowest band, not approve");
    }
}
