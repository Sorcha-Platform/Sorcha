// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Sorcha.Agent.Execution;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Tests.Execution;

public class AuditLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public AuditLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-audit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    private static ActionAuditEntry CreateEntry(string actionId = "act-1") => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        ActionId = actionId,
        ActionName = "TestAction",
        Decision = "approve",
        Success = true,
        DurationMs = 42
    };

    [Fact]
    public async Task LogAsync_CreatesFileAndAppendsEntry()
    {
        var path = Path.Combine(_tempDir, "audit.jsonl");

        // Dispose logger before reading file to release handle
        using (var logger = new AuditLogger(path))
        {
            await logger.LogAsync(CreateEntry());
        }

        File.Exists(path).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(1);

        var entry = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        entry.GetProperty("actionId").GetString().Should().Be("act-1");
        entry.GetProperty("decision").GetString().Should().Be("approve");
    }

    [Fact]
    public async Task LogAsync_AppendsMultipleEntries()
    {
        var path = Path.Combine(_tempDir, "audit-multi.jsonl");

        using (var logger = new AuditLogger(path))
        {
            await logger.LogAsync(CreateEntry("act-1"));
            await logger.LogAsync(CreateEntry("act-2"));
            await logger.LogAsync(CreateEntry("act-3"));
        }

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task LogAsync_CreatesDirectoryIfMissing()
    {
        var path = Path.Combine(_tempDir, "subdir", "nested", "audit.jsonl");

        using (var logger = new AuditLogger(path))
        {
            await logger.LogAsync(CreateEntry());
        }

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task LogAsync_NullPath_DoesNothing()
    {
        using var logger = new AuditLogger(null);
        // Should not throw
        await logger.LogAsync(CreateEntry());
    }

    [Fact]
    public async Task LogAsync_NullError_OmittedFromJson()
    {
        var path = Path.Combine(_tempDir, "audit-null.jsonl");

        using (var logger = new AuditLogger(path))
        {
            await logger.LogAsync(CreateEntry());
        }

        var json = await File.ReadAllTextAsync(path);
        json.Should().NotContain("\"error\"");
    }
}
