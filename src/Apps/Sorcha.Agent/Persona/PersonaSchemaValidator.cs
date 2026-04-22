// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Validates a parsed persona JSON file against the embedded persona-schema.json.
/// </summary>
public sealed class PersonaSchemaValidator
{
    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    /// <summary>
    /// Returns a flat list of schema violations. Empty result means the file is valid.
    /// </summary>
    public IReadOnlyList<string> Validate(JsonElement personaJson)
    {
        var options = new EvaluationOptions { OutputFormat = OutputFormat.List };
        var result = Schema.Value.Evaluate(personaJson, options);
        if (result.IsValid) return Array.Empty<string>();

        var errors = new List<string>();
        CollectErrors(result, errors);
        return errors;
    }

    private static void CollectErrors(EvaluationResults result, List<string> errors)
    {
        if (!result.IsValid && result.Errors is { Count: > 0 })
        {
            foreach (var kv in result.Errors)
                errors.Add($"{result.InstanceLocation}: {kv.Value}");
        }
        if (result.Details is { Count: > 0 })
        {
            foreach (var detail in result.Details)
                CollectErrors(detail, errors);
        }
    }

    private static JsonSchema LoadSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Sorcha.Agent.Persona.Schemas.persona-schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' not found");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
