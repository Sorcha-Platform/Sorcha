// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Loads and validates a persona definition JSON file.
/// Surfaces all errors (schema, token, variable) at load time — FR-010 / FR-014.
/// </summary>
public static class PersonaDefinitionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static PersonaLoadResult Load(string personaFilePath, string? statePath = null)
    {
        if (!File.Exists(personaFilePath))
            return PersonaLoadResult.Failure([$"Persona file not found: {personaFilePath}"]);

        string rawJson;
        try { rawJson = File.ReadAllText(personaFilePath); }
        catch (Exception ex) { return PersonaLoadResult.Failure([$"Failed to read persona file: {ex.Message}"]); }

        var errors = new List<string>();

        var stateValues = VariableResolver.LoadStateFile(statePath);
        var resolution = VariableResolver.Resolve(rawJson, stateValues);
        if (resolution.HasUnresolved)
            foreach (var u in resolution.UnresolvedVariables)
                errors.Add($"Unresolved variable: {u}");

        JsonNode? root;
        try { root = JsonNode.Parse(resolution.ResolvedJson); }
        catch (JsonException ex) { return PersonaLoadResult.Failure([$"Invalid JSON: {ex.Message}"]); }
        if (root is null)
            return PersonaLoadResult.Failure(["Persona file is empty"]);

        var schemaErrors = new PersonaSchemaValidator().Validate(
            JsonDocument.Parse(root.ToJsonString()).RootElement);
        errors.AddRange(schemaErrors);

        if (root["payloadTemplate"] is JsonNode template)
        {
            var tokenErrors = new PayloadTokenResolver().ValidateTokens(template);
            errors.AddRange(tokenErrors);
        }

        if (errors.Count > 0)
            return PersonaLoadResult.Failure(errors);

        PersonaDefinition definition;
        try
        {
            definition = root.Deserialize<PersonaDefinition>(JsonOptions)
                ?? throw new JsonException("Deserialization returned null");
        }
        catch (JsonException ex)
        {
            return PersonaLoadResult.Failure([$"Failed to deserialize persona: {ex.Message}"]);
        }

        errors.AddRange(ValidateSemantics(definition));
        if (errors.Count > 0)
            return PersonaLoadResult.Failure(errors);

        return PersonaLoadResult.Ok(definition);
    }

    private static IEnumerable<string> ValidateSemantics(PersonaDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Name)) yield return "name is required";
        if (string.IsNullOrWhiteSpace(def.Target.BlueprintId)) yield return "target.blueprintId is required";
        if (string.IsNullOrWhiteSpace(def.Target.InstanceId)) yield return "target.instanceId is required";
        if (def.Target.ActionName is null && def.Target.ActionIndex is null)
            yield return "target must include actionName or actionIndex";
        if (def.Target.ActionIndex is int idx && idx < 0)
            yield return "target.actionIndex must be >= 0";

        switch (def.Trigger)
        {
            case OnceTrigger once:
                if (once.DelaySeconds < 0) yield return "trigger.delaySeconds must be >= 0";
                break;
            case IntervalTrigger interval:
                if ((interval.EverySeconds is null) == (interval.EveryMinutes is null))
                    yield return "interval trigger must set exactly one of everySeconds or everyMinutes";
                if (interval.EverySeconds is int s && s <= 0) yield return "trigger.everySeconds must be > 0";
                if (interval.EveryMinutes is int m && m <= 0) yield return "trigger.everyMinutes must be > 0";
                if (interval.MaxIterations is int mi && mi <= 0) yield return "trigger.maxIterations must be > 0";
                break;
        }
    }
}

/// <summary>
/// Result of a persona load. Mirrors <see cref="ActorDefinitionLoadResult"/>.
/// </summary>
public record PersonaLoadResult
{
    public PersonaDefinition? Definition { get; init; }
    public List<string> Errors { get; init; } = [];
    public bool IsSuccess => Errors.Count == 0 && Definition is not null;

    public static PersonaLoadResult Ok(PersonaDefinition definition) => new() { Definition = definition };
    public static PersonaLoadResult Failure(List<string> errors) => new() { Errors = errors };
}
