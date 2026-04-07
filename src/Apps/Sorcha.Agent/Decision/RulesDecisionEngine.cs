// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Decision;

/// <summary>
/// Evaluates JSON Logic rules against incoming actions. First match wins.
/// </summary>
public class RulesDecisionEngine : IDecisionEngine
{
    private readonly ActorRule[] _rules;
    private readonly ILogger<RulesDecisionEngine>? _logger;

    public RulesDecisionEngine(ActorRule[] rules, ILogger<RulesDecisionEngine>? logger = null)
    {
        _rules = rules;
        _logger = logger;
    }

    public Task<ActionDecision> DecideAsync(PendingAction action, CancellationToken cancellationToken = default)
    {
        foreach (var rule in _rules)
        {
            // Match by action name
            if (!string.Equals(rule.ActionName, action.ActionName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Evaluate condition (null/missing condition = always true)
            if (rule.Condition is not null)
            {
                var data = BuildContextData(action);
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

            return Task.FromResult(new ActionDecision(
                rule.Decision,
                payload,
                $"Rule matched: {rule.ActionName} → {rule.Decision}"));
        }

        // No match
        _logger?.LogWarning("No rule matched for action {ActionName}, skipping", action.ActionName);
        return Task.FromResult(new ActionDecision("skip", null, "No matching rule"));
    }

    private static bool EvaluateCondition(JsonNode condition, JsonNode data)
    {
        try
        {
            var rule = JsonSerializer.Deserialize<Rule>(condition.ToJsonString());
            if (rule is null) return true;

            var result = rule.Apply(data);
            return IsTruthy(result);
        }
        catch
        {
            return false;
        }
    }

    private static JsonNode BuildContextData(PendingAction action)
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
