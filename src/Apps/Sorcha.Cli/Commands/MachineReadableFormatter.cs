// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Wraps output in a standard JSON envelope for machine consumption.
/// </summary>
public class MachineReadableFormatter : IOutputFormatter
{
    private readonly string _command;

    public MachineReadableFormatter(string command)
    {
        _command = command;
    }

    /// <inheritdoc/>
    public string FormatSingle<T>(T data) where T : class
    {
        return CreateEnvelope("success", data, null);
    }

    /// <inheritdoc/>
    public string FormatCollection<T>(IEnumerable<T> data) where T : class
    {
        return CreateEnvelope("success", data, null);
    }

    /// <inheritdoc/>
    public string FormatMessage(string message)
    {
        return CreateEnvelope("success", new { message }, null);
    }

    /// <summary>
    /// Creates an error envelope.
    /// </summary>
    public string FormatError(string[] errors, int exitCode)
    {
        var envelope = new
        {
            status = "error",
            command = _command,
            data = (object?)null,
            errors,
            timestamp = DateTime.UtcNow.ToString("O"),
            exitCode
        };
        return JsonSerializer.Serialize(envelope, SorchaJsonOptions.Default);
    }

    private string CreateEnvelope(string status, object? data, string[]? errors)
    {
        var envelope = new
        {
            status,
            command = _command,
            data,
            errors = errors ?? Array.Empty<string>(),
            timestamp = DateTime.UtcNow.ToString("O"),
            exitCode = 0
        };
        return JsonSerializer.Serialize(envelope, SorchaJsonOptions.Default);
    }
}
