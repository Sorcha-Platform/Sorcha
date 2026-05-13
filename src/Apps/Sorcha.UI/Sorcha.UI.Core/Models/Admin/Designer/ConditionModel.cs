// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// UI model for building routing conditions visually.
/// </summary>
public class ConditionModel
{
    public List<ConditionClause> Clauses { get; set; } = [];
    public LogicalOperator Operator { get; set; } = LogicalOperator.And;
    public string? TargetParticipantId { get; set; }

    /// <summary>
    /// Converts the visual condition model to JSON Logic format.
    /// </summary>
    public JsonNode? ToJsonLogic()
    {
        if (Clauses.Count == 0) return null;
        if (Clauses.Count == 1) return Clauses[0].ToJsonLogic();

        var op = Operator == LogicalOperator.And ? "and" : "or";
        var clauseNodes = Clauses.Select(c => c.ToJsonLogic()).ToArray();
        return JsonNode.Parse($"{{\"{op}\":{JsonSerializer.Serialize(clauseNodes)}}}");
    }

    /// <summary>
    /// Parses a JSON Logic expression into a visual condition model.
    /// </summary>
    public static ConditionModel FromJsonLogic(JsonNode? node)
    {
        var model = new ConditionModel();

        if (node is null) return model;

        if (node is JsonObject obj)
        {
            // Check for logical operators (and/or)
            if (obj.ContainsKey("and"))
            {
                model.Operator = LogicalOperator.And;
                ParseClauses(obj["and"], model);
            }
            else if (obj.ContainsKey("or"))
            {
                model.Operator = LogicalOperator.Or;
                ParseClauses(obj["or"], model);
            }
            else
            {
                // Single clause
                var clause = ParseSingleClause(obj);
                if (clause is not null)
                {
                    model.Clauses.Add(clause);
                }
            }
        }

        return model;
    }

    private static void ParseClauses(JsonNode? clausesNode, ConditionModel model)
    {
        if (clausesNode is not JsonArray array) return;

        foreach (var item in array)
        {
            if (item is JsonObject clauseObj)
            {
                var clause = ParseSingleClause(clauseObj);
                if (clause is not null)
                {
                    model.Clauses.Add(clause);
                }
            }
        }
    }

    private static ConditionClause? ParseSingleClause(JsonObject obj)
    {
        foreach (var (opKey, opValue) in obj)
        {
            var compOp = opKey switch
            {
                "==" => ComparisonOperator.Equals,
                "!=" => ComparisonOperator.NotEquals,
                ">" => ComparisonOperator.GreaterThan,
                "<" => ComparisonOperator.LessThan,
                ">=" => ComparisonOperator.GreaterOrEqual,
                "<=" => ComparisonOperator.LessOrEqual,
                "in" => ComparisonOperator.Contains,
                _ => (ComparisonOperator?)null
            };

            if (compOp is null || opValue is not JsonArray args || args.Count < 2) continue;

            var fieldPath = string.Empty;
            var value = string.Empty;
            var valueType = ConditionFieldType.String;

            // First arg should be field reference
            if (args[0] is JsonObject fieldRef && fieldRef.ContainsKey("var"))
            {
                fieldPath = fieldRef["var"]?.GetValue<string>() ?? string.Empty;
            }

            // Second arg is the value
            var valueNode = args[1];
            if (valueNode is JsonValue jv)
            {
                if (jv.TryGetValue<decimal>(out var numVal))
                {
                    value = numVal.ToString();
                    valueType = ConditionFieldType.Number;
                }
                else if (jv.TryGetValue<bool>(out var boolVal))
                {
                    value = boolVal.ToString().ToLowerInvariant();
                    valueType = ConditionFieldType.Boolean;
                }
                else
                {
                    value = jv.GetValue<string>() ?? string.Empty;
                    valueType = ConditionFieldType.String;
                }
            }

            return new ConditionClause
            {
                FieldPath = fieldPath,
                Operator = compOp.Value,
                Value = value,
                ValueType = valueType
            };
        }

        return null;
    }
}

/// <summary>
/// A single condition clause comparing a field to a value.
/// </summary>
public class ConditionClause
{
    /// <summary>JSON Pointer path to the field (e.g., "/loanAmount").</summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>The comparison operator to use.</summary>
    public ComparisonOperator Operator { get; set; } = ComparisonOperator.Equals;

    /// <summary>The value to compare against.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>The data type of the value.</summary>
    public ConditionFieldType ValueType { get; set; } = ConditionFieldType.String;

    /// <summary>
    /// Converts this clause to JSON Logic format.
    /// </summary>
    public JsonNode ToJsonLogic()
    {
        var op = Operator switch
        {
            ComparisonOperator.Equals => "==",
            ComparisonOperator.NotEquals => "!=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.GreaterOrEqual => ">=",
            ComparisonOperator.LessOrEqual => "<=",
            ComparisonOperator.Contains => "in",
            ComparisonOperator.StartsWith => "startsWith",
            ComparisonOperator.EndsWith => "endsWith",
            _ => "=="
        };

        var fieldRef = new { var = FieldPath };
        object typedValue = ValueType switch
        {
            ConditionFieldType.Number => decimal.TryParse(Value, out var d) ? d : 0m,
            ConditionFieldType.Boolean => bool.TryParse(Value, out var b) && b,
            _ => Value
        };

        return JsonNode.Parse($"{{\"{op}\":[{JsonSerializer.Serialize(fieldRef)},{JsonSerializer.Serialize(typedValue)}]}}")!;
    }
}

/// <summary>Logical operators for combining condition clauses.</summary>
public enum LogicalOperator
{
    And,
    Or
}

/// <summary>Comparison operators for condition clauses.</summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Contains,
    StartsWith,
    EndsWith
}

/// <summary>Data types for condition values.</summary>
public enum ConditionFieldType
{
    String,
    Number,
    Boolean,
    Date
}
