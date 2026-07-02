// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Extensions;

/// <summary>
/// Shared JSON serializer options for API deserialization across UI services.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Standard options for deserializing API responses (case-insensitive property matching).
    /// </summary>
    public static readonly JsonSerializerOptions Api = CreateApiOptions();

    private static JsonSerializerOptions CreateApiOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        // The services serialize enums as kebab-case strings (JsonStringEnumConverter with
        // KebabCaseLower — e.g. PersonaAttributeSource -> "self-asserted"). Without a matching
        // converter here, deserialising such a response throws and the caller silently gets an empty
        // result (the "My Profile is empty even though it saved" class of bug). The converter also
        // reads numeric enum values, so it is safe for any endpoint that emits numbers.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
