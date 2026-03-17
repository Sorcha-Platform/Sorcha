// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Schemas.DTOs;

/// <summary>
/// Represents a schema result from an external source.
/// </summary>
/// <param name="Name">The display name of the schema.</param>
/// <param name="Description">A brief description of the schema.</param>
/// <param name="Url">The URL where the schema can be fetched.</param>
/// <param name="Provider">The provider name (e.g., "SchemaStore.org").</param>
/// <param name="FileMatch">Optional file patterns this schema applies to (e.g., "package.json").</param>
/// <param name="Versions">Optional list of available versions.</param>
/// <param name="Content">The full JSON Schema content (populated when fetching individual schema).</param>
/// <param name="SectorTags">Optional per-schema sector tags. When set, overrides the provider's default sector tags.</param>
public record ExternalSchemaResult(
    string Name,
    string Description,
    string Url,
    string Provider,
    IReadOnlyList<string>? FileMatch = null,
    IReadOnlyList<string>? Versions = null,
    JsonDocument? Content = null,
    string[]? SectorTags = null);
