// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Reads the platform-owned system blueprint templates shipped with the service and computes the
/// publication id each one would receive (Feature 197, #1466).
/// </summary>
public interface ISystemBlueprintCatalogSource
{
    /// <summary>Loads the shipped template for <paramref name="id"/>, or null when none is found.</summary>
    JsonElement? TryLoad(string id);

    /// <summary>
    /// The publication id a publish of the shipped template would produce, or null when the
    /// template is not shipped.
    /// </summary>
    string? TryComputePublicationId(string id);
}

/// <summary>
/// File-backed <see cref="ISystemBlueprintCatalogSource"/> over <c>blueprints/templates/{id}.json</c>.
/// </summary>
public sealed class SystemBlueprintCatalogSource : ISystemBlueprintCatalogSource
{
    /// <inheritdoc />
    public JsonElement? TryLoad(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "blueprints", "templates", $"{id}.json"),
            Path.Combine("/blueprints", "templates", $"{id}.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "blueprints", "templates", $"{id}.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            return doc.RootElement.TryGetProperty("template", out var template)
                ? template.Clone()
                : doc.RootElement.Clone();
        }

        return null;
    }

    /// <inheritdoc />
    public string? TryComputePublicationId(string id)
    {
        if (TryLoad(id) is not { } definition) return null;

        return SystemRegisterService.ComputePublicationId(id, definition);
    }
}
