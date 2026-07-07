// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision;
using Sorcha.Agent.Decision.Checks;
using Sorcha.Agent.Models;
using Sorcha.Agent.Tests.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision;

/// <summary>
/// Feature 176 US3 — every decision is explainable. After the agent decides, the evaluated
/// <c>checks.*</c> facts and the payload fields they were derived from are emitted as a structured log,
/// and for a rejection the failing check is identifiable from those facts. The original defect was
/// invisible precisely because the agent surfaced nothing about what it evaluated.
/// </summary>
public class CheckFactsObservabilityTests
{
    private const string ActionName = "Verify Assured Identity Application";

    private sealed class FactCheck(string name, bool value) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => Task.FromResult(new ExternalCheckResult(name, value, null));
    }

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

    private static PendingAction Action() => new()
    {
        ActionId = "a", ActionName = ActionName, ActionIndex = 2,
        BlueprintId = "bp", InstanceId = "i", RegisterId = "r", TransactionId = "t",
        PreviousPayload = JsonSerializer.Deserialize<JsonElement>(
            """{ "name": { "fullName": "Ada Lovelace" }, "address": { "postcode": "SW1A 1AA" } }"""),
    };

    private static (RulesDecisionEngine engine, List<string> logs) EngineWithCapturedLogs(params IExternalCheck[] checks)
    {
        var logs = new List<string>();
        var factory = LoggerFactory.Create(b =>
            b.AddProvider(new CapturingLoggerProvider((_, _, message) =>
            {
                lock (logs) { logs.Add(message); }
            })));
        var engine = new RulesDecisionEngine(Rules(), new ExternalCheckRunner(checks), factory.CreateLogger<RulesDecisionEngine>());
        return (engine, logs);
    }

    [Fact]
    public async Task AfterDecision_EvaluatedFactsAndSourceFields_AreEmittedStructured()
    {
        var (engine, logs) = EngineWithCapturedLogs(
            new FactCheck("postcodeExists", true), new FactCheck("emailVerified", true));

        await engine.DecideAsync(Action());

        var factsLog = logs.Should().ContainSingle(l => l.Contains("External checks evaluated")).Subject;
        factsLog.Should().Contain("postcodeExists", "the evaluated facts must be visible");
        factsLog.Should().Contain("address", "the source payload fields must be visible");
        factsLog.Should().Contain("name");
    }

    [Fact]
    public async Task ForRejection_FailingCheckIsIdentifiable()
    {
        var (engine, logs) = EngineWithCapturedLogs(
            new FactCheck("postcodeExists", false), new FactCheck("emailVerified", true));

        var decision = await engine.DecideAsync(Action());

        decision.Decision.Should().Be("reject");
        logs.Should().Contain(
            l => l.Contains("External checks evaluated") && l.Contains("\"postcodeExists\":false"),
            "the specific failing check must be identifiable from the recorded facts");
    }
}
