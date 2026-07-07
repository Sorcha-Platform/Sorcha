// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Decision.Checks;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Decision;

/// <summary>
/// Evaluates JSON Logic rules against incoming actions. First match wins. When an optional
/// <see cref="ExternalCheckRunner"/> is supplied, its checks run before rule evaluation and their
/// boolean results are merged into the rules context under the <c>checks</c> key, so rules can
/// reference facts like <c>{ "var": "checks.postcodeExists" }</c>. Agents without configured checks
/// are unaffected (the runner is null or has no checks).
/// </summary>
public class RulesDecisionEngine : IDecisionEngine
{
    private readonly ActorRule[] _rules;
    private readonly IExternalCheckRunner? _checkRunner;
    private readonly ILogger<RulesDecisionEngine>? _logger;

    // #1077: true when ANY rule condition references a "checks.*" fact. When the rules depend on
    // external checks we must FAIL CLOSED if no check facts are available (runner absent / no checks
    // configured / HasChecks false / faulted) — otherwise the "checks.X == false" reject rules
    // silently no-op (null != false in strict ==) and the catch-all approve fires, issuing a
    // credential with zero verification. Computed once — the rules are fixed for the engine's life.
    private readonly bool _rulesRequireChecks;

    /// <summary>Creates a rules engine over <paramref name="rules"/> with no external checks.</summary>
    public RulesDecisionEngine(ActorRule[] rules, ILogger<RulesDecisionEngine>? logger = null)
        : this(rules, null, logger)
    {
    }

    /// <summary>
    /// Creates a rules engine that runs <paramref name="checkRunner"/> before evaluating rules,
    /// merging the resulting facts under the <c>checks</c> context key.
    /// </summary>
    public RulesDecisionEngine(
        ActorRule[] rules, IExternalCheckRunner? checkRunner, ILogger<RulesDecisionEngine>? logger = null)
    {
        _rules = rules;
        _checkRunner = checkRunner;
        _logger = logger;

        // A rule "requires checks" if its serialised condition references a "checks." fact path
        // (e.g. { "var": "checks.postcodeExists" }). Inclusive by design: a false positive only ever
        // causes a conservative hold (fail-safe), whereas a miss would re-open the zero-verification
        // hole. Agents with no such rules are entirely unaffected.
        _rulesRequireChecks = rules.Any(r =>
            r.Condition is not null
            && r.Condition.ToJsonString().Contains("checks.", StringComparison.Ordinal));
    }

    public async Task<ActionDecision> DecideAsync(PendingAction action, CancellationToken cancellationToken = default)
    {
        // Run configured external checks once and expose them as the "checks" fact object. Skipped
        // entirely when no runner/checks are configured, so non-AIAS agents pay nothing.
        JsonObject? checksFacts = null;
        if (_checkRunner is { HasChecks: true })
        {
            checksFacts = await BuildChecksFactsAsync(action, cancellationToken);

            // Runner-level fault: BuildChecksFactsAsync returns null when the entire runner throws.
            // Fail closed — do NOT proceed to rules evaluation with missing check facts, because the
            // catch-all approve rule would fire unconditionally (JSON Logic null != false in strict ==).
            if (checksFacts is null)
            {
                _logger?.LogError(
                    "External-check runner faulted for action {ActionName}; holding for manual review",
                    action.ActionName);
                return new ActionDecision("hold", null, "External checks unavailable; application held for manual review");
            }
        }

        // #1077: fail closed when the rules DEPEND on external checks but no check facts are available.
        // This covers the absence modes the block above does NOT: a null runner, a runner with
        // HasChecks == false, or no ChecksFile configured (e.g. a stale agent build) — any of which
        // leaves checksFacts null while the reject rules reference checks.*. Without this guard those
        // rejects silently no-op and the catch-all approve issues a credential with zero verification
        // (observed live: fake postcode "ZZ99 9ZZ" approved). Agents whose rules don't reference
        // checks.* have _rulesRequireChecks == false and are unaffected.
        if (checksFacts is null && _rulesRequireChecks)
        {
            _logger?.LogError(
                "Rules for action {ActionName} reference external checks but no check facts are available "
                + "(no check runner configured/available); holding for manual review (fail-closed, #1077).",
                action.ActionName);
            return new ActionDecision("hold", null,
                "External checks are required by policy but were unavailable; application held for manual review");
        }

        foreach (var rule in _rules)
        {
            // Match by action name
            if (!string.Equals(rule.ActionName, action.ActionName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Evaluate condition (null/missing condition = always true)
            if (rule.Condition is not null)
            {
                var data = BuildContextData(action, checksFacts);
                if (!EvaluateCondition(rule.Condition, data))
                {
                    _logger?.LogDebug("Rule condition not met for action {ActionName}", action.ActionName);
                    continue;
                }
            }

            // First match wins
            _logger?.LogInformation("Rule matched for action {ActionName}: {Decision}", action.ActionName, rule.Decision);

            var payload = rule.Payload is not null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(rule.Payload.ToJsonString())
                : null;

            return new ActionDecision(
                rule.Decision,
                payload,
                $"Rule matched: {rule.ActionName} → {rule.Decision}",
                rule.PreActions);
        }

        // No match
        _logger?.LogWarning("No rule matched for action {ActionName}, skipping", action.ActionName);
        return new ActionDecision("skip", null, "No matching rule");
    }

    /// <summary>
    /// Runs the configured external checks against the action's submitted payload and returns the
    /// merged facts as a JSON object suitable for the <c>checks</c> context key. Returns
    /// <see langword="null"/> when the runner throws — callers must treat null as a fail-closed signal
    /// and return a <c>hold</c> decision rather than proceeding to rule evaluation.
    /// </summary>
    private async Task<JsonObject?> BuildChecksFactsAsync(PendingAction action, CancellationToken ct)
    {
        var payload = ExtractPayload(action);

        IReadOnlyDictionary<string, object?> facts;
        try
        {
            facts = await _checkRunner!.RunAsync(payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger?.LogError(ex, "External-check runner threw for action {ActionName}; returning null to trigger fail-closed hold", action.ActionName);
            return null;
        }

        var obj = new JsonObject();
        foreach (var (key, value) in facts)
        {
            obj[key] = value switch
            {
                bool b => JsonValue.Create(b),
                string s => JsonValue.Create(s),
                null => null,
                _ => JsonValue.Create(value.ToString())
            };
        }

        return obj;
    }

    /// <summary>Projects the action's submitted (previous) payload into the top-level dictionary the checks consume.</summary>
    private static IReadOnlyDictionary<string, object?> ExtractPayload(PendingAction action)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (action.PreviousPayload is { } prev
            && JsonNode.Parse(prev.GetRawText()) is JsonObject obj)
        {
            foreach (var (key, value) in obj)
                payload[key] = value;
        }

        return payload;
    }

    private bool EvaluateCondition(JsonNode condition, JsonNode data)
    {
        try
        {
            var rule = JsonSerializer.Deserialize<Rule>(condition.ToJsonString());
            if (rule is null) return true;

            var result = rule.Apply(data);
            return IsTruthy(result);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "JSON Logic condition evaluation failed");
            return false;
        }
    }

    private static JsonNode BuildContextData(PendingAction action, JsonObject? checksFacts)
    {
        var data = new JsonObject
        {
            ["action"] = new JsonObject
            {
                ["name"] = action.ActionName,
                ["index"] = action.ActionIndex
            }
        };

        // Add previous payload under "payload" key for var references like "payload.estimatedCost"
        if (action.PreviousPayload.HasValue)
        {
            var payloadNode = JsonNode.Parse(action.PreviousPayload.Value.GetRawText());
            data["payload"] = payloadNode;
        }

        // Expose external-check facts under "checks" for var references like "checks.postcodeExists".
        // Deep-cloned because the same fact set is attached to a fresh context per rule iteration.
        if (checksFacts is not null)
            data["checks"] = checksFacts.DeepClone();

        return data;
    }

    private static bool IsTruthy(JsonNode? node)
    {
        if (node is null) return false;

        return node switch
        {
            JsonValue value when value.TryGetValue(out bool b) => b,
            JsonValue value when value.TryGetValue(out int i) => i != 0,
            JsonValue value when value.TryGetValue(out double d) => d != 0,
            JsonValue value when value.TryGetValue(out string? s) => !string.IsNullOrEmpty(s),
            _ => true
        };
    }
}
