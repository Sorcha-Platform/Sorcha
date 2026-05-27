// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Service for schema operations: merging, scope resolution, validation, and auto-generation.
/// </summary>
public interface IFormSchemaService
{
    /// <summary>
    /// Merges multiple DataSchemas into a single combined schema
    /// </summary>
    JsonDocument? MergeSchemas(IEnumerable<JsonDocument>? schemas);

    /// <summary>
    /// Extracts the sub-schema for a specific JSON Pointer scope
    /// </summary>
    JsonElement? GetSchemaForScope(JsonDocument? schema, string scope);

    /// <summary>
    /// Normalizes a scope string (ensures leading /)
    /// </summary>
    string NormalizeScope(string scope);

    /// <summary>
    /// Validates all form data against the merged schema.
    /// Returns scope-keyed error messages.
    /// </summary>
    Dictionary<string, List<string>> ValidateData(JsonDocument? schema, Dictionary<string, object?> data);

    /// <summary>
    /// Validates a single field by scope.
    /// Returns error messages for that field.
    /// </summary>
    List<string> ValidateField(JsonDocument? schema, string scope, object? value);

    /// <summary>
    /// Gets enum values from a schema property
    /// </summary>
    List<string> GetEnumValues(JsonDocument? schema, string scope);

    /// <summary>
    /// Checks if a field is required
    /// </summary>
    bool IsRequired(JsonDocument? schema, string scope);

    /// <summary>
    /// Auto-generates a Control tree from DataSchemas when Action.Form is null
    /// </summary>
    Control AutoGenerateForm(IEnumerable<JsonDocument>? schemas);
}
