// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sorcha.Agent.Configuration;

/// <summary>
/// Resolves $env:VAR_NAME and {{placeholder}} tokens in actor definition JSON.
/// </summary>
public static partial class VariableResolver
{
    private static readonly Regex EnvVarPattern = EnvVarRegex();
    private static readonly Regex PlaceholderPattern = PlaceholderRegex();

    /// <summary>
    /// Resolves all variables in a JSON string, returning the resolved string and any unresolved variables.
    /// </summary>
    public static VariableResolutionResult Resolve(string json, Dictionary<string, string>? stateValues = null)
    {
        var unresolved = new List<string>();

        // Resolve $env:VAR_NAME
        var result = EnvVarPattern.Replace(json, match =>
        {
            var varName = match.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(varName);
            if (value is null)
            {
                unresolved.Add($"$env:{varName}");
                return match.Value;
            }
            return value;
        });

        // Resolve {{placeholder}}
        result = PlaceholderPattern.Replace(result, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (stateValues is not null && stateValues.TryGetValue(key, out var value))
            {
                return value;
            }
            unresolved.Add($"{{{{{key}}}}}");
            return match.Value;
        });

        return new VariableResolutionResult(result, unresolved);
    }

    /// <summary>
    /// Loads state values from a state.json file.
    /// </summary>
    public static Dictionary<string, string>? LoadStateFile(string? statePath)
    {
        if (string.IsNullOrEmpty(statePath) || !File.Exists(statePath))
            return null;

        var json = File.ReadAllText(statePath);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        FlattenJsonElement(doc.RootElement, "", result);
        return result;
    }

    private static void FlattenJsonElement(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    // Also store without prefix for simple key access
                    if (!string.IsNullOrEmpty(prefix))
                        result.TryAdd(property.Name, property.Value.ToString()!);
                    FlattenJsonElement(property.Value, key, result);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJsonElement(item, $"{prefix}[{index}]", result);
                    index++;
                }
                break;
            default:
                var value = element.ToString() ?? "";
                if (!string.IsNullOrEmpty(prefix))
                    result.TryAdd(prefix, value);
                break;
        }
    }

    [GeneratedRegex(@"\$env:([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex EnvVarRegex();

    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex PlaceholderRegex();
}

/// <summary>
/// Result of variable resolution containing the resolved JSON and any unresolved variables.
/// </summary>
public record VariableResolutionResult(string ResolvedJson, List<string> UnresolvedVariables)
{
    public bool HasUnresolved => UnresolvedVariables.Count > 0;
}
