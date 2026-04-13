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
    /// <remarks>
    /// <b>Returned tree is the live stored reference, not a clone.</b>
    /// <see cref="JsonNode"/> instances are mutable. The resolver (and any other
    /// caller that merges, transcludes, or reshapes the primitive) MUST call
    /// <see cref="JsonNode.DeepClone"/> on the result before mutating it.
    /// Mutating the returned tree in place would corrupt subsequent resolutions
    /// for the same primitive across the entire service instance. Lookups are
    /// case-sensitive per RFC 3986 (JSON Schema <c>$id</c> URI semantics).
    /// </remarks>
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
    /// <remarks>
    /// Same mutability rule as <see cref="Get"/>: the returned dictionary is a
    /// new wrapper, but the <see cref="JsonNode"/> values are the live stored
    /// references. Callers that inspect a primitive are fine; callers that
    /// mutate one MUST <see cref="JsonNode.DeepClone"/> first. Diagnostics and
    /// listing endpoints typically serialise, not mutate, so this is usually a
    /// non-issue — but the warning is load-bearing for any future caller that
    /// enumerates with intent to transform.
    /// </remarks>
    IReadOnlyDictionary<string, JsonNode> GetAll();
}
