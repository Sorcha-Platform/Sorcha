// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Sorcha.Blueprint.Engine.Caching;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// JSON Logic expression evaluator for calculations and conditional routing.
/// </summary>
/// <remarks>
/// This implementation uses Json.Logic.Net to evaluate JSON Logic expressions
/// as defined at https://jsonlogic.com
///
/// Thread-safe and can be used concurrently.
/// Optionally uses JsonLogicCache to avoid repeated deserialization of the same expression.
/// </remarks>
public class JsonLogicEvaluator : IJsonLogicEvaluator
{
    private readonly JsonLogicCache? _cache;

    public JsonLogicEvaluator() { }

    public JsonLogicEvaluator(JsonLogicCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Evaluate a JSON Logic expression against data.
    /// </summary>
    public object Evaluate(JsonNode expression, Dictionary<string, object> data)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            // Parse the JSON Logic rule (with optional caching)
            var expressionString = expression.ToJsonString();
            Rule? rule;

            if (_cache != null)
            {
                rule = _cache.GetOrAdd(expression, expr =>
                    JsonSerializer.Deserialize<Rule>(expr.ToJsonString())!);
            }
            else
            {
                rule = JsonSerializer.Deserialize<Rule>(expressionString);
            }

            if (rule == null)
            {
                throw new InvalidOperationException("Failed to parse JSON Logic expression");
            }

            // Convert data dictionary to JsonNode
            var dataJson = ConvertToJsonNode(data);

            // Apply the rule to the data
            var result = rule.Apply(dataJson);

            // Convert result back to object
            return ConvertFromJsonNode(result);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON Logic expression: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Error evaluating JSON Logic expression: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Maximum number of calculations allowed per action execution (SEC-AUDIT 3.5).
    /// Prevents DoS via blueprints with excessive calculation chains.
    /// </summary>
    private const int MaxCalculationsPerExecution = 100;

    /// <summary>
    /// Apply multiple calculations to data.
    /// </summary>
    public async Task<Dictionary<string, object>> ApplyCalculationsAsync(
        Dictionary<string, object> data,
        IEnumerable<Calculation> calculations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(calculations);

        // Create a copy of the data to avoid modifying the original
        var result = new Dictionary<string, object>(data);

        // Apply calculations in order with iteration guard (SEC-AUDIT 3.5)
        var iterationCount = 0;
        foreach (var calculation in calculations)
        {
            ct.ThrowIfCancellationRequested();

            if (++iterationCount > MaxCalculationsPerExecution)
            {
                throw new InvalidOperationException(
                    $"Calculation limit exceeded: maximum {MaxCalculationsPerExecution} calculations per execution. " +
                    "This may indicate a malicious or misconfigured blueprint.");
            }

            try
            {
                // Evaluate the calculation expression against current data
                var value = Evaluate(calculation.Expression, result);

                // Add or update the output field
                result[calculation.OutputField] = value;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Error applying calculation for field '{calculation.OutputField}': {ex.Message}",
                    ex);
            }
        }

        return await Task.FromResult(result);
    }

    /// <summary>
    /// Evaluate routing conditions to determine next participant.
    /// </summary>
    public async Task<string?> EvaluateConditionsAsync(
        Dictionary<string, object> data,
        IEnumerable<Condition> conditions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(conditions);

        // Evaluate each condition in order
        foreach (var condition in conditions)
        {
            ct.ThrowIfCancellationRequested();

            // Each condition can have multiple criteria (JSON Logic expressions)
            // All criteria must evaluate to true for the condition to match
            bool allCriteriaMatch = true;

            foreach (var criteriaJson in condition.Criteria)
            {
                try
                {
                    // Parse the criteria JSON string to JsonNode
                    var criteriaNode = JsonNode.Parse(criteriaJson);
                    if (criteriaNode == null)
                    {
                        allCriteriaMatch = false;
                        break;
                    }

                    // Evaluate the criteria
                    var result = Evaluate(criteriaNode, data);

                    // Check if result is truthy
                    if (!IsTruthy(result))
                    {
                        allCriteriaMatch = false;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Error evaluating condition for participant '{condition.Principal}': {ex.Message}",
                        ex);
                }
            }

            // If all criteria matched, return this participant
            if (allCriteriaMatch && condition.Criteria.Any())
            {
                return condition.Principal;
            }
        }

        // No conditions matched
        return null;
    }

    /// <summary>
    /// Converts a dictionary to JsonNode for JSON Logic evaluation.
    /// </summary>
    private static JsonNode ConvertToJsonNode(Dictionary<string, object> data)
    {
        var json = JsonSerializer.Serialize(data);
        return JsonNode.Parse(json) ?? new JsonObject();
    }

    /// <summary>
    /// Converts a JsonNode result back to a .NET object.
    /// </summary>
    private static object ConvertFromJsonNode(JsonNode? node)
    {
        if (node == null) return null!;

        return node switch
        {
            JsonValue value => GetJsonValueAsObject(value),
            JsonObject obj => JsonSerializer.Deserialize<Dictionary<string, object>>(obj.ToJsonString()) ?? new Dictionary<string, object>(),
            JsonArray arr => JsonSerializer.Deserialize<List<object>>(arr.ToJsonString()) ?? new List<object>(),
            _ => node.ToJsonString()
        };
    }

    /// <summary>
    /// Converts a JsonValue to the appropriate .NET type.
    /// </summary>
    private static object GetJsonValueAsObject(JsonValue value)
    {
        // Try to get the underlying value kind
        if (value.TryGetValue(out bool boolValue))
            return boolValue;
        if (value.TryGetValue(out int intValue))
            return intValue;
        if (value.TryGetValue(out long longValue))
            return longValue;
        if (value.TryGetValue(out double doubleValue))
            return doubleValue;
        if (value.TryGetValue(out decimal decimalValue))
            return (double)decimalValue;
        if (value.TryGetValue(out string? stringValue) && stringValue != null)
            return stringValue;

        // Fallback: try to extract from JsonElement (wrapped numeric values)
        if (value.TryGetValue(out JsonElement element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => element.GetString() ?? "",
                _ => element.ToString()
            };
        }

        // Final fallback to string representation
        return value.ToString();
    }

    /// <summary>
    /// Determines if a value is "truthy" for condition evaluation.
    /// </summary>
    /// <remarks>
    /// Truthy values:
    /// - true (boolean)
    /// - non-zero numbers
    /// - non-empty strings
    /// - non-empty arrays/collections
    /// - non-null objects
    /// 
    /// Falsy values:
    /// - false (boolean)
    /// - 0 (number)
    /// - "" (empty string)
    /// - null
    /// - empty arrays/collections
    /// </remarks>
    private static bool IsTruthy(object? value)
    {
        if (value == null) return false;

        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0.0,
            decimal dec => dec != 0,
            string s => !string.IsNullOrEmpty(s),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Any(),
            _ => true // Non-null objects are truthy
        };
    }
}
