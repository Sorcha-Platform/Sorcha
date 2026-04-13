// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Read/write registry of Sorcha core identity primitives (reusable schema
/// components referenced from blueprints via JSON Schema <c>$ref</c>).
/// </summary>
/// <remarks>
/// <para>
/// Populated at startup by <see cref="CoreSchemaSeedService"/> from JSON files
/// under <c>blueprints/schemas/sorcha-core/</c>. Consumed by the schema
/// <c>$ref</c> resolver during blueprint validation and rendering.
/// </para>
/// <para>
/// Implementations are intentionally tiny — keyed lookup by the primitive's
/// HTTPS <c>$id</c>, no search or indexing. External schema discovery (for
/// Schema.org / FHIR / etc.) lives in the separate <c>SchemaIndex</c> stack.
/// </para>
/// </remarks>
public interface ICoreSchemaRepository
{
    /// <summary>
    /// Look up a primitive by its HTTPS <c>$id</c>. Returns <c>null</c> when
    /// unknown.
    /// </summary>
    JsonNode? Get(string id);

    /// <summary>
    /// Upsert a primitive under its <c>$id</c>. Called by the seed service at
    /// startup; not intended for runtime callers.
    /// </summary>
    void Upsert(string id, JsonNode schema);

    /// <summary>
    /// Enumerate every primitive currently registered. Used by diagnostics and
    /// the schema-library listing endpoint.
    /// </summary>
    IReadOnlyDictionary<string, JsonNode> GetAll();
}
