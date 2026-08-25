// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Agent.Configuration;

/// <summary>
/// Loads and validates actor definition JSON files.
/// </summary>
public static class ActorDefinitionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads an actor definition from a JSON file, resolving variables from environment and state.
    /// </summary>
    public static ActorDefinitionLoadResult Load(string configPath, string? statePath = null)
    {
        var errors = new List<string>();

        // Check file exists
        if (!File.Exists(configPath))
        {
            return ActorDefinitionLoadResult.Failure([$"Config file not found: {configPath}"]);
        }

        // Read raw JSON
        string rawJson;
        try
        {
            rawJson = File.ReadAllText(configPath);
        }
        catch (Exception ex)
        {
            return ActorDefinitionLoadResult.Failure([$"Failed to read config file: {ex.Message}"]);
        }

        // Load state values
        var stateValues = VariableResolver.LoadStateFile(statePath);

        // Resolve variables
        var resolution = VariableResolver.Resolve(rawJson, stateValues);
        if (resolution.HasUnresolved)
        {
            foreach (var unresolved in resolution.UnresolvedVariables)
            {
                errors.Add($"Unresolved variable: {unresolved}");
            }
        }

        // Deserialize
        ActorDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<ActorDefinition>(resolution.ResolvedJson, JsonOptions)
                ?? throw new JsonException("Deserialization returned null");
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
            return ActorDefinitionLoadResult.Failure(errors);
        }

        // Validate
        errors.AddRange(Validate(definition));

        if (errors.Count > 0)
            return ActorDefinitionLoadResult.Failure(errors);

        return ActorDefinitionLoadResult.Ok(definition);
    }

    private static List<string> Validate(ActorDefinition definition)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Actor.Name))
            errors.Add("actor.name is required");

        if (string.IsNullOrWhiteSpace(definition.Connection.GatewayUrl))
            errors.Add("connection.gatewayUrl is required");

        if (string.IsNullOrWhiteSpace(definition.Connection.RegisterId))
            errors.Add("connection.registerId is required");

        if (string.IsNullOrWhiteSpace(definition.Connection.WalletAddress))
            errors.Add("connection.walletAddress is required");

        if (string.IsNullOrWhiteSpace(definition.Connection.Credentials.Email))
            errors.Add("connection.credentials.email is required");

        if (string.IsNullOrWhiteSpace(definition.Connection.Credentials.Password))
            errors.Add("connection.credentials.password is required");

        if (string.IsNullOrWhiteSpace(definition.Connection.Credentials.OrganizationId))
            errors.Add("connection.credentials.organizationId is required");

        // Mode validation
        if (definition.Mode is not "rules" and not "ai")
        {
            errors.Add($"mode must be 'rules' or 'ai', got '{definition.Mode}'");
        }
        else if (definition.Mode == "rules" && (definition.Rules is null || definition.Rules.Length == 0))
        {
            errors.Add("mode is 'rules' but no rules are defined");
        }
        else if (definition.Mode == "ai" && definition.Ai is null)
        {
            errors.Add("mode is 'ai' but ai config is not defined");
        }

        // Inbox validation — at least one channel must be enabled
        var signalREnabled = definition.Inbox.SignalR?.Enabled ?? false;
        var pollingEnabled = definition.Inbox.Polling?.Enabled ?? false;
        if (!signalREnabled && !pollingEnabled)
        {
            errors.Add("At least one inbox channel (signalR or polling) must be enabled");
        }

        // Issue #1446 — an open-starting watch with no blueprint has nothing to ask for, and the
        // endpoint refuses an unscoped query. Caught here so `sorcha-agent validate` reports it
        // rather than the agent starting and silently never seeing anything.
        if ((definition.Inbox.OpenStarting?.Enabled ?? false)
            && string.IsNullOrWhiteSpace(definition.Inbox.OpenStarting!.BlueprintId))
        {
            errors.Add(OpenStartingConfig.BlueprintIdRequiredError);
        }

        return errors;
    }
}

/// <summary>
/// Result of loading an actor definition.
/// </summary>
public record ActorDefinitionLoadResult
{
    public ActorDefinition? Definition { get; init; }
    public List<string> Errors { get; init; } = [];
    public bool IsSuccess => Errors.Count == 0 && Definition is not null;

    public static ActorDefinitionLoadResult Ok(ActorDefinition definition) =>
        new() { Definition = definition };

    public static ActorDefinitionLoadResult Failure(List<string> errors) =>
        new() { Errors = errors };
}
