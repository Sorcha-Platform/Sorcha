// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Loads and validates a persona definition JSON file.
/// Surfaces all errors (schema, token, variable) at load time — FR-010 / FR-014.
/// </summary>
/// <remarks>
/// Variable resolution is delegated to <see cref="VariableResolver"/>, which handles both
/// <c>$env:NAME</c> (environment variables) and <c>{{key}}</c> (state.json flattened keys)
/// — the same contract that actor configs use. This is the pre-existing type in
/// <c>Sorcha.Agent.Configuration</c>; no new resolver is introduced by this feature.
/// </remarks>
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

    // Must match JsonOptions for the up-front JsonDocument/JsonNode.Parse calls —
    // without these flags the loader rejects JSONC-style `//` comments that we
    // ship for human tuning (Feature 110 T040).
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
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

        JsonDocument? doc;
        try { doc = JsonDocument.Parse(resolution.ResolvedJson, DocOptions); }
        catch (JsonException ex) { return PersonaLoadResult.Failure([$"Invalid JSON: {ex.Message}"]); }
        using (doc)
        {
            var schemaErrors = new PersonaSchemaValidator().Validate(doc.RootElement);
            errors.AddRange(schemaErrors);
        }

        JsonNode? root;
        try { root = JsonNode.Parse(resolution.ResolvedJson, documentOptions: DocOptions); }
        catch (JsonException ex) { return PersonaLoadResult.Failure([$"Invalid JSON: {ex.Message}"]); }
        if (root is null)
            return PersonaLoadResult.Failure(["Persona file is empty"]);

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

    // Accepts alphanumerics, dashes, underscores, dots, and colons — the character set
    // the Blueprint Service emits for IDs. Rejects path separators and URL-meaningful
    // characters to prevent env-var-sourced values from rewriting the request endpoint.
    private static readonly Regex SafeIdPattern = new(@"^[A-Za-z0-9._:-]{1,128}$", RegexOptions.Compiled);

    private static IEnumerable<string> ValidateSemantics(PersonaDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Name)) yield return "name is required";
        if (string.IsNullOrWhiteSpace(def.Target.BlueprintId)) yield return "target.blueprintId is required";
        else if (!SafeIdPattern.IsMatch(def.Target.BlueprintId))
            yield return $"target.blueprintId=\"{def.Target.BlueprintId}\" contains disallowed characters (must match {SafeIdPattern})";
        if (string.IsNullOrWhiteSpace(def.Target.InstanceId)) yield return "target.instanceId is required";
        else if (!SafeIdPattern.IsMatch(def.Target.InstanceId))
            yield return $"target.instanceId=\"{def.Target.InstanceId}\" contains disallowed characters (must match {SafeIdPattern})";
        if (def.Target.ActionName is null && def.Target.ActionIndex is null)
            yield return "target must include actionName or actionIndex";
        if (def.Target.ActionIndex is int idx && idx < 0)
            yield return "target.actionIndex must be >= 0";
        // v1 note: action-name → index resolution (blueprint fetch) is deferred to a
        // follow-up feature. Until then, a persona declared with only actionName cannot
        // be submitted, so we reject at load time rather than crash at first fire.
        if (def.Target.ActionName is not null && def.Target.ActionIndex is null)
            yield return $"target.actionName=\"{def.Target.ActionName}\" is not yet supported in v1 — set "
                       + "target.actionIndex to the zero-based position of that action in the blueprint "
                       + "(e.g. actionIndex: 0 for the starting action). Action-name lookup will land in a follow-up feature.";

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
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public bool IsSuccess => Errors.Count == 0 && Definition is not null;

    public static PersonaLoadResult Ok(PersonaDefinition definition) => new() { Definition = definition };
    public static PersonaLoadResult Failure(IReadOnlyList<string> errors) => new() { Errors = errors };
}
