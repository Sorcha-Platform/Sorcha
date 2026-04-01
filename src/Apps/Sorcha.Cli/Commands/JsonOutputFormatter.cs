// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;

namespace Sorcha.Cli.Commands;

/// <summary>
/// JSON output formatter. Produces valid JSON arrays for collections.
/// </summary>
public class JsonOutputFormatter : IOutputFormatter
{
    /// <inheritdoc/>
    public string FormatSingle<T>(T data) where T : class
    {
        return JsonSerializer.Serialize(data, SorchaJsonOptions.Default);
    }

    /// <inheritdoc/>
    public string FormatCollection<T>(IEnumerable<T> data) where T : class
    {
        return JsonSerializer.Serialize(data.ToList(), SorchaJsonOptions.Default);
    }

    /// <inheritdoc/>
    public string FormatMessage(string message)
    {
        return JsonSerializer.Serialize(new { message }, SorchaJsonOptions.Default);
    }
}
