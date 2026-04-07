// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Execution;

/// <summary>
/// Appends action audit entries to a JSONL file.
/// </summary>
public class AuditLogger : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly string? _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private StreamWriter? _writer;
    private bool _disposed;

    public AuditLogger(string? filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Logs an audit entry to the JSONL file.
    /// </summary>
    public async Task LogAsync(ActionAuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_filePath))
            return;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_writer is null)
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream) { AutoFlush = true };
            }

            var json = JsonSerializer.Serialize(entry, JsonOptions);
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writer?.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
