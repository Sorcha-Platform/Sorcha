// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Serialization;

/// <summary>
/// The single source of truth for Sorcha's JSON wire format. Both sides derive from it so they can
/// never drift:
/// <list type="bullet">
///   <item>Services apply it to their serializer via <c>AddServiceDefaults</c> (server side).</item>
///   <item>UI clients deserialize responses with <see cref="Options"/> (client side).</item>
/// </list>
/// The format is <c>System.Text.Json</c> Web defaults (camelCase properties, case-insensitive
/// matching) plus enums as <b>kebab-case strings</b> (e.g. <c>PersonaAttributeSource.SelfAsserted</c>
/// → <c>"self-asserted"</c>; also required for the WebAuthn <c>"public-key"</c> credential type). A
/// client that deserialises such a response WITHOUT this converter throws on the enum and the caller
/// silently falls back to empty — the "My Profile is blank though it saved" / "couldn't load your
/// security settings" class of bug.
/// </summary>
public static class SorchaJson
{
    /// <summary>
    /// A ready-made, read-only options instance for (de)serialisation. Prefer this over
    /// <c>JsonSerializerOptions.Default</c>/<c>.Web</c> for any Sorcha API payload.
    /// </summary>
    public static readonly JsonSerializerOptions Options = CreateReadOnly();

    /// <summary>
    /// Applies the Sorcha wire-format conventions to an existing <see cref="JsonSerializerOptions"/>
    /// (e.g. the server's <c>ConfigureHttpJsonOptions</c> instance). Idempotent — safe to call more
    /// than once and on top of Web defaults.
    /// </summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;

        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        }
    }

    private static JsonSerializerOptions CreateReadOnly()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
