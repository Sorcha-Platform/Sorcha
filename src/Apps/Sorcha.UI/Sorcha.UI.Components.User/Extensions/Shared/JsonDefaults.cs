// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Extensions;

/// <summary>
/// Shared JSON serializer options for API (de)serialization across UI services. This is a thin alias
/// over <see cref="Sorcha.Serialization.SorchaJson.Options"/> — the single source of truth shared with the
/// services — so the client and server wire formats can never drift. Prefer <c>JsonDefaults.Api</c>
/// (or <c>SorchaJson.Options</c>) for every <c>ReadFromJsonAsync</c> / <c>GetFromJsonAsync</c> on a
/// Sorcha API response; the default options do not carry the kebab-case string-enum converter and
/// silently throw on enum fields.
/// </summary>
public static class JsonDefaults
{
    /// <summary>Standard options for (de)serializing Sorcha API payloads.</summary>
    public static readonly JsonSerializerOptions Api = Sorcha.Serialization.SorchaJson.Options;
}
