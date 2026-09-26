// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// THE options the validator reads a blueprint with, from either source: the Blueprint Service's
/// HTTP API (<see cref="BlueprintFetcher"/>) or the shared Redis cache (<see cref="BlueprintCache"/>).
/// </summary>
/// <remarks>
/// <para>
/// #1700. The two sources write enums differently. The Blueprint Service calls
/// <c>SorchaJson.Configure</c>, whose kebab-case <see cref="JsonStringEnumConverter"/> sits in
/// <c>options.Converters</c> — and <c>Converters</c> outranks a type-level <c>[JsonConverter]</c>
/// attribute, so its HTTP responses say <c>"first-word"</c>. The cache copy is written without that
/// converter, so the type attribute wins and it says <c>"FirstWord"</c>. The validator read both with
/// plain options, which accept only the latter: every cold-cache fetch of a pinned definition whose
/// <c>instanceReference</c> named a transform failed, and every deploy empties the cache.
/// </para>
/// <para>
/// A kebab-case converter reads BOTH forms: the naming policy's names first, then the enum's own
/// member names case-insensitively. That is pinned by a test that feeds each form in, because the
/// fallback is what makes one options instance safe for both sources.
/// </para>
/// <para>
/// This governs READING only. Nothing here changes how a blueprint is serialised for hashing or for
/// the ledger — the canonical form (pattern 22) has its own options, and a wire-form change there
/// would move every publication id.
/// </para>
/// </remarks>
internal static class BlueprintWireJson
{
    /// <summary>Options that read a blueprint from the HTTP API or the Redis cache alike.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };
}
