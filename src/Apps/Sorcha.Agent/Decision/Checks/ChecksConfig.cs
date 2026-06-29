// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Root shape of an external-checks config file (e.g. <c>assure-id.checks.json</c>): declares which
/// checks the agent runs before evaluating its JSON-Logic rules.
/// </summary>
public sealed record ChecksConfig
{
    /// <summary>The ordered list of checks to run. Order is immaterial — checks run concurrently.</summary>
    public CheckDefinition[] Checks { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Parses a <see cref="ChecksConfig"/> from raw JSON.</summary>
    public static ChecksConfig Parse(string json) =>
        JsonSerializer.Deserialize<ChecksConfig>(json, JsonOptions)
        ?? throw new JsonException("Checks config deserialized to null");

    /// <summary>Loads a <see cref="ChecksConfig"/> from a file path.</summary>
    public static ChecksConfig Load(string path) => Parse(File.ReadAllText(path));
}

/// <summary>
/// One declared check: the stable fact <see cref="Name"/>, the <see cref="Type"/> discriminator,
/// and type-specific settings. Unused settings are ignored per type.
/// </summary>
public sealed record CheckDefinition
{
    /// <summary>Stable fact key the rules reference (e.g. <c>postcodeExists</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Discriminator: <c>email-verified</c>, <c>field-present</c>, <c>uk-postcode</c>, or <c>profanity</c>.</summary>
    public required string Type { get; init; }

    /// <summary>JSON-Pointer to a single field (<c>field-present</c>, <c>email-verified</c>).</summary>
    public string? Field { get; init; }

    /// <summary>JSON-Pointer to the address/postcode (<c>uk-postcode</c>).</summary>
    public string? AddressField { get; init; }

    /// <summary>JSON-Pointer to the email address, surfaced as detail (<c>email-verified</c>).</summary>
    public string? EmailField { get; init; }

    /// <summary>Path (relative to the config file) of the offline postcode fixture (<c>uk-postcode</c>).</summary>
    public string? OfflineFixture { get; init; }

    /// <summary>Offline reconciliation mode (<c>uk-postcode</c>): <c>auto</c> | <c>always</c> | <c>never</c>.</summary>
    public string? OfflineMode { get; init; }

    /// <summary>Override postcodes.io base URL (<c>uk-postcode</c>); defaults to the public service.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>JSON-Pointers to free-text fields to scan (<c>profanity</c>).</summary>
    public string[]? Fields { get; init; }

    /// <summary>Inline profanity wordlist (<c>profanity</c>).</summary>
    public string[]? WordlistInline { get; init; }

    /// <summary>Path (relative to the config file) of a newline-delimited wordlist file (<c>profanity</c>).</summary>
    public string? WordlistFile { get; init; }
}
