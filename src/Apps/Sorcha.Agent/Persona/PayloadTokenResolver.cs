// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Resolves persona payload tokens. See <c>specs/110-agent-persona-mode/data-model.md</c>.
/// </summary>
public sealed partial class PayloadTokenResolver : IPayloadTokenResolver
{
    // Matches ${name} or ${name(args)} — args may include quoted strings, numbers, arrays.
    [GeneratedRegex(@"\$\{([a-zA-Z_][a-zA-Z0-9_.]*)(?:\(([^}]*)\))?\}")]
    private static partial Regex TokenRegex();

    public JsonObject Resolve(JsonNode template, PersonaFireContext ctx)
    {
        // Invariant: tokens were already validated at load time via ValidateTokens.
        // Re-running validation on every fire would make recurring personas O(template)
        // per tick for no benefit. Malformed tokens surfaced here indicate a loader bug
        // and will throw when EvaluateTokenTyped encounters them.
        // DeepClone avoids a string round-trip on every fire — important for interval triggers.
        var clone = template.DeepClone();
        ResolveNode(clone, ctx);
        return clone.AsObject();
    }

    public IReadOnlyList<string> ValidateTokens(JsonNode template)
    {
        var errors = new List<string>();
        WalkStrings(template, value =>
        {
            foreach (Match match in TokenRegex().Matches(value))
            {
                var name = match.Groups[1].Value;
                var args = match.Groups[2].Success ? match.Groups[2].Value : null;
                if (!TryValidateToken(name, args, out var error))
                    errors.Add(error);
            }
        });
        return errors;
    }

    private static void ResolveNode(JsonNode? node, PersonaFireContext ctx)
    {
        switch (node)
        {
            case JsonObject obj:
                // Iterate keys snapshot so we can replace values during iteration.
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue value && value.TryGetValue<string>(out var s))
                    {
                        obj[key] = ResolveString(s, ctx);
                    }
                    else
                    {
                        ResolveNode(child, ctx);
                    }
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue value && value.TryGetValue<string>(out var s))
                    {
                        arr[i] = ResolveString(s, ctx);
                    }
                    else
                    {
                        ResolveNode(child, ctx);
                    }
                }
                break;
        }
    }

    private static JsonNode? ResolveString(string value, PersonaFireContext ctx)
    {
        var matches = TokenRegex().Matches(value);
        if (matches.Count == 0)
            return JsonValue.Create(value);

        // Single-token-and-nothing-else case: preserve typed JSON result.
        if (matches.Count == 1 && matches[0].Value == value)
        {
            return EvaluateTokenTyped(matches[0].Groups[1].Value,
                matches[0].Groups[2].Success ? matches[0].Groups[2].Value : null, ctx);
        }

        // Embedded tokens: stringify each match and interpolate.
        return JsonValue.Create(TokenRegex().Replace(value, m =>
        {
            var typed = EvaluateTokenTyped(m.Groups[1].Value,
                m.Groups[2].Success ? m.Groups[2].Value : null, ctx);
            return StringifyToken(typed);
        }));
    }

    private static JsonNode? EvaluateTokenTyped(string name, string? argsText, PersonaFireContext ctx) => name switch
    {
        "now"      => JsonValue.Create(ctx.Now.ToString("O", CultureInfo.InvariantCulture)),
        "uuid"     => JsonValue.Create(Guid.NewGuid().ToString()),
        "counter"  => JsonValue.Create(ctx.Iteration),
        "random.int"     => JsonValue.Create(EvaluateRandomInt(argsText, ctx)),
        "random.decimal" => JsonValue.Create(EvaluateRandomDecimal(argsText, ctx)),
        "random.choice"  => EvaluateRandomChoice(argsText, ctx),
        _ => throw new InvalidOperationException($"Unknown token '{name}'")
    };

    private static string StringifyToken(JsonNode? node)
    {
        if (node is null) return "";
        if (node is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return node.ToJsonString();
    }

    private static int EvaluateRandomInt(string? argsText, PersonaFireContext ctx)
    {
        var (min, max) = ParseTwoIntArgs(argsText, "random.int");
        return ctx.RandomSource.NextInt(min, max);
    }

    private static decimal EvaluateRandomDecimal(string? argsText, PersonaFireContext ctx)
    {
        var parts = SplitArgs(argsText, "random.decimal");
        if (parts.Count != 3)
            throw new InvalidOperationException($"random.decimal expects 3 args (min, max, precision); got {parts.Count}");
        var min = decimal.Parse(parts[0], CultureInfo.InvariantCulture);
        var max = decimal.Parse(parts[1], CultureInfo.InvariantCulture);
        var precision = int.Parse(parts[2], CultureInfo.InvariantCulture);
        return ctx.RandomSource.NextDecimal(min, max, precision);
    }

    private static JsonNode? EvaluateRandomChoice(string? argsText, PersonaFireContext ctx)
    {
        if (string.IsNullOrWhiteSpace(argsText))
            throw new InvalidOperationException("random.choice requires a JSON array argument");
        var trimmed = argsText.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
            throw new InvalidOperationException("random.choice argument must be a JSON array literal");

        var arr = JsonNode.Parse(trimmed)?.AsArray()
            ?? throw new InvalidOperationException("random.choice argument failed to parse as JSON array");
        if (arr.Count == 0)
            throw new InvalidOperationException("random.choice list must be non-empty");

        var items = arr.ToList();
        var picked = ctx.RandomSource.Choose(items);
        return picked?.DeepClone();
    }

    private static (int min, int max) ParseTwoIntArgs(string? argsText, string name)
    {
        var parts = SplitArgs(argsText, name);
        if (parts.Count != 2)
            throw new InvalidOperationException($"{name} expects 2 args; got {parts.Count}");
        return (int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    // Splits "1, 2, 3" or "1, 2, [\"a\", \"b\"]" — recognises [...] as one chunk.
    // Known limitation: does not honour quoted strings containing unescaped commas.
    // Not hit by the current token vocabulary (int/decimal args are numeric; random.choice
    // passes its whole [...] as one chunk so inner commas are handled by JSON parsing).
    // Revisit if/when a token accepts a bare quoted-string list at the top level.
    private static List<string> SplitArgs(string? argsText, string name)
    {
        if (string.IsNullOrWhiteSpace(argsText))
            throw new InvalidOperationException($"{name} requires arguments");

        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < argsText.Length; i++)
        {
            var c = argsText[i];
            if (c == '[') depth++;
            else if (c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(argsText[start..i].Trim());
                start = i + 1;
            }
        }
        result.Add(argsText[start..].Trim());
        return result;
    }

    private static bool TryValidateToken(string name, string? argsText, out string error)
    {
        try
        {
            switch (name)
            {
                case "now":
                case "uuid":
                case "counter":
                    if (!string.IsNullOrEmpty(argsText))
                    {
                        error = $"Token '{name}' does not accept arguments";
                        return false;
                    }
                    break;
                case "random.int":
                    ParseTwoIntArgs(argsText, "random.int");
                    break;
                case "random.decimal":
                    var parts = SplitArgs(argsText, "random.decimal");
                    if (parts.Count != 3) throw new InvalidOperationException($"random.decimal expects 3 args; got {parts.Count}");
                    decimal.Parse(parts[0], CultureInfo.InvariantCulture);
                    decimal.Parse(parts[1], CultureInfo.InvariantCulture);
                    int.Parse(parts[2], CultureInfo.InvariantCulture);
                    break;
                case "random.choice":
                    if (string.IsNullOrWhiteSpace(argsText))
                        throw new InvalidOperationException("random.choice requires a JSON array argument");
                    var t = argsText.Trim();
                    if (!t.StartsWith('[') || !t.EndsWith(']'))
                        throw new InvalidOperationException("random.choice argument must be a JSON array literal");
                    var arr = JsonNode.Parse(t)?.AsArray()
                        ?? throw new InvalidOperationException("random.choice argument failed to parse as JSON array");
                    if (arr.Count == 0)
                        throw new InvalidOperationException("random.choice list must be non-empty");
                    break;
                default:
                    error = $"Unknown token '{name}'";
                    return false;
            }
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid token '${{{name}{(argsText is null ? "" : "(" + argsText + ")")}}}': {ex.Message}";
            return false;
        }
    }

    private static void WalkStrings(JsonNode? node, Action<string> visit)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var p in obj)
                {
                    if (p.Value is JsonValue v && v.TryGetValue<string>(out var s)) visit(s);
                    else WalkStrings(p.Value, visit);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (item is JsonValue v && v.TryGetValue<string>(out var s)) visit(s);
                    else WalkStrings(item, visit);
                }
                break;
            case JsonValue val when val.TryGetValue<string>(out var s):
                visit(s);
                break;
        }
    }
}
