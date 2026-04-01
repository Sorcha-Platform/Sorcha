// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Centralised JSON serializer options for consistent output across all commands.
/// </summary>
public static class SorchaJsonOptions
{
    /// <summary>
    /// Default options: indented, camelCase.
    /// </summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Compact options: no indentation, camelCase. For JSON lines output.
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
