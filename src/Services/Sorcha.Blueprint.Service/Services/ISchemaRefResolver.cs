// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Flattens JSON Schema documents that reference Sorcha core identity
/// primitives via <c>$ref</c>. After flattening the consumer (validator,
/// renderer) sees a single self-contained schema with all reusable
/// components inlined.
/// </summary>
/// <remarks>
/// <para>
/// Recognised URI form: <c>https://schemas.sorcha.dev/core/{Name}/v{N}</c>.
/// Resolution falls through to <see cref="ICoreSchemaRepository"/>.
/// </para>
/// <para>
/// Layout merge rule (per
/// <c>specs/103-verified-citizen-v2/contracts/identity-primitive-format.md</c>):
/// </para>
/// <list type="bullet">
///   <item>The consuming site (where the <c>$ref</c> appears) MAY declare
///   <c>x-pages</c> / <c>x-sections</c> / <c>x-introduction</c> / <c>x-width</c>
///   as siblings to <c>$ref</c> — these are layout overrides.</item>
///   <item>On resolution, the component's full body is inlined at the site
///   (replacing the <c>$ref</c>), then the captured layout overrides are
///   reapplied so that child wins for layout.</item>
///   <item>The component owns <c>type</c> / <c>properties</c> / <c>required</c>
///   / per-property metadata (<c>x-persona</c>, <c>x-address-lookup</c>,
///   <c>format</c>, <c>formatMinimum</c>/<c>formatMaximum</c> etc.) — these
///   cannot be overridden inline; the consumer must reference a different
///   primitive version if they need different validation.</item>
/// </list>
/// <para>
/// Cycle detection is per <c>Flatten</c> call: a primitive that references
/// itself (directly or transitively) raises
/// <see cref="SchemaRefResolutionException"/>. Unknown URIs and unimplemented
/// URI schemes (e.g. <c>did:sorcha:register:...</c>) raise the same exception.
/// </para>
/// </remarks>
public interface ISchemaRefResolver
{
    /// <summary>
    /// Returns a deep clone of <paramref name="rootSchema"/> with all Sorcha
    /// core <c>$ref</c>s flattened in place. The input tree is never mutated.
    /// </summary>
    /// <exception cref="SchemaRefResolutionException">
    /// One or more <c>$ref</c>s could not be resolved — unknown URI, cycle
    /// detected, or unsupported URI scheme.
    /// </exception>
    JsonNode Flatten(JsonNode rootSchema);
}
