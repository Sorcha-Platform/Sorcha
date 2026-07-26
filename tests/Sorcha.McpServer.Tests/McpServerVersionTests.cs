// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.McpServer.Infrastructure;

namespace Sorcha.McpServer.Tests;

/// <summary>
/// MCP audit 2026-07-26 — <c>ServerInfo.Version</c> was hardcoded <c>"1.0.0"</c>, so an MCP
/// client's <c>initialize</c> saw <c>1.0.0</c> while the manifest at <c>/.well-known/mcp.json</c>
/// reported the real build (<c>2.886.1</c> on n1 at the time). Two version claims about the same
/// server, one of them false — and a violation of the unified-versioning rule (CLAUDE.md §14:
/// display via the derived assembly version, never a hardcoded string).
/// </summary>
public class McpServerVersionTests
{
    [Fact]
    public void Current_DerivesFromTheAssemblyInformationalVersion()
    {
        // Derived from the same attribute the build stamps — NOT a hand-kept expectation. Locally
        // this is "2.0.0-dev"; in CI it is "2.<run>.<attempt>". Either way it must match exactly,
        // which fails if anyone re-hardcodes the constant.
        var raw = typeof(McpServerVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var plusIdx = raw.IndexOf('+');
        var expected = plusIdx > 0 ? raw[..plusIdx] : raw;

        McpServerVersion.Current.Should().Be(expected);
    }

    [Fact]
    public void Current_IsNotTheOldHardcodedPlaceholder()
    {
        // The root Directory.Build.props derives 2.x for every component (§14), so a "1.0.0" here
        // can only mean the resolution silently fell back — the exact symptom being fixed.
        McpServerVersion.Current.Should().NotBe("1.0.0");
        McpServerVersion.Current.Should().StartWith("2.");
    }

    [Fact]
    public void Current_CarriesNoCommitHashSuffix()
    {
        // The informational version is "2.x.y+<sha>" when SourceLink stamps it; the "+sha" tail is
        // build metadata, not a version a client should compare against.
        McpServerVersion.Current.Should().NotContain("+");
    }
}
