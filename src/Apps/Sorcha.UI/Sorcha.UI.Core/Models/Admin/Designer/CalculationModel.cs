// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// UI model for calculated field expressions.
/// </summary>
public class CalculationModel
{
    /// <summary>JSON Pointer path to the target field that will hold the calculated value.</summary>
    public string TargetFieldPath { get; set; } = string.Empty;

    /// <summary>The elements that make up the calculation expression.</summary>
    public List<CalculationElement> Elements { get; set; } = [];

    /// <summary>
    /// Converts the calculation model to JSON Logic format.
    /// </summary>
    public JsonNode? ToJsonLogic()
    {
        if (Elements.Count == 0) return null;

        // Build expression tree from infix notation
        var output = new Stack<JsonNode>();
        var operators = new Stack<CalculationElement>();

        foreach (var element in Elements)
        {
            switch (element.Type)
            {
                case CalculationElementType.Field:
                    output.Push(JsonNode.Parse($"{{\"var\":\"{element.FieldPath}\"}}")!);
                    break;

                case CalculationElementType.Constant:
                    if (decimal.TryParse(element.ConstantValue, out var value))
                    {
                        output.Push(JsonValue.Create(value));
                    }
                    break;

                case CalculationElementType.OpenParen:
                    operators.Push(element);
                    break;

                case CalculationElementType.CloseParen:
                    while (operators.Count > 0 && operators.Peek().Type != CalculationElementType.OpenParen)
                    {
                        ApplyOperator(output, operators.Pop());
                    }
                    if (operators.Count > 0) operators.Pop(); // Remove the open paren
                    break;

                case CalculationElementType.Operator:
                    while (operators.Count > 0 &&
                           operators.Peek().Type == CalculationElementType.Operator &&
                           GetPrecedence(operators.Peek().Operator) >= GetPrecedence(element.Operator))
                    {
                        ApplyOperator(output, operators.Pop());
                    }
                    operators.Push(element);
                    break;
            }
        }

        while (operators.Count > 0)
        {
            ApplyOperator(output, operators.Pop());
        }

        return output.Count > 0 ? output.Pop() : null;
    }

    private static void ApplyOperator(Stack<JsonNode> output, CalculationElement op)
    {
        if (output.Count < 2 || op.Operator is null) return;

        var right = output.Pop();
        var left = output.Pop();

        var opString = op.Operator switch
        {
            ArithmeticOperator.Add => "+",
            ArithmeticOperator.Subtract => "-",
            ArithmeticOperator.Multiply => "*",
            ArithmeticOperator.Divide => "/",
            _ => "+"
        };

        var result = JsonNode.Parse($"{{\"{opString}\":[{left.ToJsonString()},{right.ToJsonString()}]}}")!;
        output.Push(result);
    }

    private static int GetPrecedence(ArithmeticOperator? op) => op switch
    {
        ArithmeticOperator.Add => 1,
        ArithmeticOperator.Subtract => 1,
        ArithmeticOperator.Multiply => 2,
        ArithmeticOperator.Divide => 2,
        _ => 0
    };

    /// <summary>
    /// Parses a JSON Logic expression into a calculation model.
    /// </summary>
    public static CalculationModel FromJsonLogic(JsonNode? node)
    {
        var model = new CalculationModel();
        if (node is null) return model;

        ParseNode(node, model.Elements);
        return model;
    }

    private static void ParseNode(JsonNode node, List<CalculationElement> elements)
    {
        if (node is JsonObject obj)
        {
            // Check for field reference
            if (obj.ContainsKey("var"))
            {
                elements.Add(new CalculationElement
                {
                    Type = CalculationElementType.Field,
                    FieldPath = obj["var"]?.GetValue<string>()
                });
                return;
            }

            // Check for arithmetic operators
            foreach (var (opKey, opValue) in obj)
            {
                var op = opKey switch
                {
                    "+" => ArithmeticOperator.Add,
                    "-" => ArithmeticOperator.Subtract,
                    "*" => ArithmeticOperator.Multiply,
                    "/" => ArithmeticOperator.Divide,
                    _ => (ArithmeticOperator?)null
                };

                if (op is not null && opValue is JsonArray args && args.Count >= 2)
                {
                    elements.Add(new CalculationElement { Type = CalculationElementType.OpenParen });
                    ParseNode(args[0]!, elements);
                    elements.Add(new CalculationElement
                    {
                        Type = CalculationElementType.Operator,
                        Operator = op
                    });
                    ParseNode(args[1]!, elements);
                    elements.Add(new CalculationElement { Type = CalculationElementType.CloseParen });
                }
            }
        }
        else if (node is JsonValue jv)
        {
            if (jv.TryGetValue<decimal>(out var numVal))
            {
                elements.Add(new CalculationElement
                {
                    Type = CalculationElementType.Constant,
                    ConstantValue = numVal.ToString()
                });
            }
        }
    }

    /// <summary>
    /// Evaluates the calculation with sample test values.
    /// </summary>
    public decimal? Evaluate(Dictionary<string, object> testValues)
    {
        var jsonLogic = ToJsonLogic();
        if (jsonLogic is null) return null;

        try
        {
            return EvaluateNode(jsonLogic, testValues);
        }
        catch
        {
            return null;
        }
    }

    private static decimal? EvaluateNode(JsonNode node, Dictionary<string, object> testValues)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("var"))
            {
                var path = obj["var"]?.GetValue<string>();
                if (path is not null && testValues.TryGetValue(path, out var value))
                {
                    return Convert.ToDecimal(value);
                }
                return null;
            }

            foreach (var (opKey, opValue) in obj)
            {
                if (opValue is not JsonArray args || args.Count < 2) continue;

                var left = EvaluateNode(args[0]!, testValues);
                var right = EvaluateNode(args[1]!, testValues);

                if (left is null || right is null) return null;

                return opKey switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" when right != 0 => left / right,
                    _ => null
                };
            }
        }
        else if (node is JsonValue jv && jv.TryGetValue<decimal>(out var numVal))
        {
            return numVal;
        }

        return null;
    }
}

/// <summary>
/// A single element in a calculation expression.
/// </summary>
public class CalculationElement
{
    public CalculationElementType Type { get; set; }

    /// <summary>For field references, the JSON Pointer path.</summary>
    public string? FieldPath { get; set; }

    /// <summary>For constants, the numeric value as a string.</summary>
    public string? ConstantValue { get; set; }

    /// <summary>For operators, the arithmetic operation.</summary>
    public ArithmeticOperator? Operator { get; set; }
}

/// <summary>Types of elements in a calculation expression.</summary>
public enum CalculationElementType
{
    Field,
    Constant,
    Operator,
    OpenParen,
    CloseParen
}

/// <summary>Arithmetic operators for calculations.</summary>
public enum ArithmeticOperator
{
    Add,
    Subtract,
    Multiply,
    Divide
}
