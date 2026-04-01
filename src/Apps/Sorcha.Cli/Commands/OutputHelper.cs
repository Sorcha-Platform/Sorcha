// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Static helper for consistent output formatting across all commands.
/// Commands that don't extend BaseCommand can use these methods directly.
/// </summary>
public static class OutputHelper
{
    /// <summary>
    /// Gets the output format from the current parse result.
    /// </summary>
    public static string GetOutputFormat(ParseResult parseResult)
    {
        return (BaseCommand.OutputOption != null ? parseResult.GetValue(BaseCommand.OutputOption) : null)
            ?? "table";
    }

    /// <summary>
    /// Gets the appropriate formatter for the current output format.
    /// </summary>
    public static IOutputFormatter GetFormatter(string outputFormat)
    {
        return outputFormat.ToLowerInvariant() switch
        {
            "json" => new JsonOutputFormatter(),
            "csv" => new CsvOutputFormatter(),
            "yaml" => new YamlOutputFormatter(),
            "table" => new TableOutputFormatter(),
            _ => new TableOutputFormatter()
        };
    }

    /// <summary>
    /// Gets a formatter from the parse result, including --machine-readable support.
    /// </summary>
    public static IOutputFormatter GetFormatter(ParseResult parseResult)
    {
        if (BaseCommand.MachineReadableOption != null && parseResult.GetValue(BaseCommand.MachineReadableOption))
        {
            var commandName = parseResult.CommandResult.Command.Name;
            return new MachineReadableFormatter(commandName);
        }

        return GetFormatter(GetOutputFormat(parseResult));
    }

    /// <summary>
    /// Formats and writes a single object in the requested format.
    /// </summary>
    public static void WriteSingle<T>(ParseResult parseResult, T data) where T : class
    {
        var formatter = GetFormatter(parseResult);
        Console.WriteLine(formatter.FormatSingle(data));
    }

    /// <summary>
    /// Formats and writes a collection in the requested format.
    /// </summary>
    public static void WriteCollection<T>(ParseResult parseResult, IEnumerable<T> data) where T : class
    {
        var formatter = GetFormatter(parseResult);
        Console.WriteLine(formatter.FormatCollection(data));
    }

    /// <summary>
    /// Checks if the output format is non-table (structured data format).
    /// </summary>
    public static bool IsStructuredFormat(string outputFormat)
    {
        return outputFormat.ToLowerInvariant() is "json" or "csv" or "yaml";
    }
}
