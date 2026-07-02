// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Extensions;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for exporting and importing blueprint instruction strings
/// as flat key-value JSON for translation workflows.
/// Export format: { "blueprint.overview": "...", "action.0.instructions": "...", ... }
/// </summary>
public class InstructionExportService
{
    /// <summary>
    /// Exports all instruction strings from a blueprint as a flat key-value dictionary.
    /// Keys follow the pattern: blueprint.overview, action.{id}.instructions,
    /// participant.{name}.instructions, etc.
    /// </summary>
    /// <param name="blueprint">The blueprint to export instructions from.</param>
    /// <returns>JSON string of flattened instruction key-value pairs.</returns>
    public string Export(BlueprintModel blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);

        var entries = new Dictionary<string, string>();

        // Blueprint-level overview
        if (!string.IsNullOrWhiteSpace(blueprint.Instructions?.Overview))
        {
            entries["blueprint.overview"] = blueprint.Instructions.Overview;
        }

        // Per-action instructions (from BlueprintInstructions.ActionInstructions)
        if (blueprint.Instructions?.ActionInstructions is not null)
        {
            foreach (var (actionId, text) in blueprint.Instructions.ActionInstructions)
            {
                entries[$"action.{actionId}.instructions"] = text;
            }
        }

        // Per-action inline instructions (from Action.Instructions property)
        foreach (var action in blueprint.Actions)
        {
            if (!string.IsNullOrWhiteSpace(action.Instructions))
            {
                entries[$"action.{action.Id}.inline"] = action.Instructions;
            }
        }

        // Per-participant instructions
        if (blueprint.Instructions?.ParticipantInstructions is not null)
        {
            foreach (var (name, text) in blueprint.Instructions.ParticipantInstructions)
            {
                entries[$"participant.{name}.instructions"] = text;
            }
        }

        return JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Imports instruction strings from a flat key-value JSON, updating the blueprint.
    /// If the locale matches the primary locale (or is null), updates inline text.
    /// If the locale is different, creates or updates an InstructionSet entry.
    /// </summary>
    /// <param name="blueprint">The blueprint to update.</param>
    /// <param name="json">JSON string of flattened instruction key-value pairs.</param>
    /// <param name="locale">BCP 47 locale tag for the imported strings (null = primary).</param>
    /// <returns>The updated blueprint.</returns>
    public BlueprintModel Import(BlueprintModel blueprint, string json, string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Api)
            ?? throw new JsonException("Failed to deserialize instruction JSON");

        var isPrimaryLocale = string.IsNullOrWhiteSpace(locale) ||
            string.Equals(locale, blueprint.Instructions?.Locale, StringComparison.OrdinalIgnoreCase);

        if (isPrimaryLocale)
        {
            ImportPrimary(blueprint, entries);
        }
        else
        {
            ImportForeignLocale(blueprint, entries, locale!);
        }

        return blueprint;
    }

    private static void ImportPrimary(BlueprintModel blueprint, Dictionary<string, string> entries)
    {
        blueprint.Instructions ??= new BlueprintInstructions();

        foreach (var (key, value) in entries)
        {
            if (key == "blueprint.overview")
            {
                blueprint.Instructions.Overview = value;
            }
            else if (key.StartsWith("action.") && key.EndsWith(".instructions"))
            {
                var actionIdStr = key["action.".Length..key.LastIndexOf('.')];
                if (int.TryParse(actionIdStr, out var actionId))
                {
                    blueprint.Instructions.ActionInstructions ??= new Dictionary<int, string>();
                    blueprint.Instructions.ActionInstructions[actionId] = value;
                }
            }
            else if (key.StartsWith("action.") && key.EndsWith(".inline"))
            {
                var actionIdStr = key["action.".Length..key.LastIndexOf('.')];
                if (int.TryParse(actionIdStr, out var actionId))
                {
                    var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionId);
                    if (action is not null)
                    {
                        action.Instructions = value;
                    }
                }
            }
            else if (key.StartsWith("participant.") && key.EndsWith(".instructions"))
            {
                var name = key["participant.".Length..key.LastIndexOf('.')];
                blueprint.Instructions.ParticipantInstructions ??= new Dictionary<string, string>();
                blueprint.Instructions.ParticipantInstructions[name] = value;
            }
        }
    }

    private static void ImportForeignLocale(BlueprintModel blueprint, Dictionary<string, string> entries, string locale)
    {
        blueprint.Instructions ??= new BlueprintInstructions();
        blueprint.Instructions.InstructionSets ??= [];

        // Store the translated content as an inline JSON source reference
        var jsonContent = JsonSerializer.Serialize(entries);
        var dataUri = $"data:application/json;base64,{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(jsonContent))}";

        var existing = blueprint.Instructions.InstructionSets
            .FirstOrDefault(s => string.Equals(s.Locale, locale, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Source = dataUri;
            existing.Version = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        }
        else
        {
            blueprint.Instructions.InstructionSets.Add(new InstructionSet
            {
                Locale = locale,
                Source = dataUri,
                Version = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd")
            });
        }
    }
}
