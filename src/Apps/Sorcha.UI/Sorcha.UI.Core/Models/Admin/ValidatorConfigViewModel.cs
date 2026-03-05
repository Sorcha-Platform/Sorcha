// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model for read-only validator configuration display.
/// </summary>
public record ValidatorConfigViewModel
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = new();

    [JsonPropertyName("redactedKeys")]
    public string[] RedactedKeys { get; init; } = [];
}
