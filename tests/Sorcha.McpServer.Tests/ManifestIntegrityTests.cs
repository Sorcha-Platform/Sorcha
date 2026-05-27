// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;

namespace Sorcha.McpServer.Tests;

/// <summary>
/// Spec 139 US5 (T040/T041/T042) — the advertised MCP tool catalogue cannot drift from the
/// server. These tests reflect the authoritative <c>[McpServerTool(Name = "...")]</c> set from the
/// <c>Sorcha.McpServer</c> assembly (post-US4 reconciliation, post-T029 <c>wallet_sign</c> removal)
/// and assert that BOTH the gateway <c>appsettings.json</c> <c>McpManifest</c> catalogue AND the
/// repo-root <c>server.json</c> tool list match it <b>exactly</b> — failing on drift in either
/// direction (a tool added/removed without updating a manifest, or a manifest entry with no tool).
/// <para>
/// This test lives in <c>Sorcha.McpServer.Tests</c>, so it runs under the <c>build-and-test</c> CI
/// gate — no separate workflow is needed (satisfies T042).
/// </para>
/// </summary>
public class ManifestIntegrityTests
{
    /// <summary>The authoritative served tool set: every discovered <c>[McpServerTool]</c> name.</summary>
    private static IReadOnlySet<string> ServedToolNames()
    {
        var assembly = Assembly.Load("Sorcha.McpServer");
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            // A tool is only discovered/registered when its declaring type is [McpServerToolType]
            // (the assembly scan). T029 removed that attribute from WalletSignTool, so it is absent.
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr?.Name is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    [Fact]
    public void ServedSurface_ExcludesWalletSign_AndIsNonEmpty()
    {
        var served = ServedToolNames();

        served.Should().NotBeEmpty("the MCP server must advertise at least one tool");
        served.Should().NotContain("sorcha_wallet_sign",
            "T029 de-registered wallet_sign — it must be fully absent from the served surface");
    }

    [Fact]
    public void GatewayManifestCatalogue_MatchesServedSurface_Exactly()
    {
        var served = ServedToolNames();
        var catalogue = GatewayCatalogueToolNames();

        AssertSetsEqual(
            served,
            catalogue,
            source: "the gateway appsettings.json McpManifest.Tools catalogue",
            fixHint: "update src/Services/Sorcha.ApiGateway/appsettings.json McpManifest.Tools");
    }

    [Fact]
    public void ServerJson_ToolList_MatchesServedSurface_Exactly()
    {
        var served = ServedToolNames();
        var serverJson = ServerJsonToolNames();

        AssertSetsEqual(
            served,
            serverJson,
            source: "the repo-root server.json tools list",
            fixHint: "update server.json \"tools\"");
    }

    private static void AssertSetsEqual(
        IReadOnlySet<string> served, IReadOnlySet<string> advertised, string source, string fixHint)
    {
        var missingFromManifest = served.Except(advertised).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var staleInManifest = advertised.Except(served).OrderBy(n => n, StringComparer.Ordinal).ToList();

        missingFromManifest.Should().BeEmpty(
            $"these tools are served by Sorcha.McpServer but are NOT advertised in {source}. {fixHint}: {string.Join(", ", missingFromManifest)}");

        staleInManifest.Should().BeEmpty(
            $"these tools are advertised in {source} but are NOT served by Sorcha.McpServer (drift). {fixHint}: {string.Join(", ", staleInManifest)}");
    }

    private static IReadOnlySet<string> GatewayCatalogueToolNames()
    {
        var path = Path.Combine(RepoRoot(), "src", "Services", "Sorcha.ApiGateway", "appsettings.json");
        File.Exists(path).Should().BeTrue($"the gateway appsettings.json must exist at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var tools = doc.RootElement.GetProperty("McpManifest").GetProperty("Tools");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools.EnumerateArray())
        {
            names.Add(tool.GetProperty("Name").GetString()!);
        }

        return names;
    }

    private static IReadOnlySet<string> ServerJsonToolNames()
    {
        var path = Path.Combine(RepoRoot(), "server.json");
        File.Exists(path).Should().BeTrue($"server.json must exist at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var tools = doc.RootElement.GetProperty("tools");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools.EnumerateArray())
        {
            names.Add(tool.GetString()!);
        }

        return names;
    }

    /// <summary>
    /// Walks up from the test assembly location to the repository root (identified by the
    /// repo-root <c>server.json</c> sibling of the <c>src</c> directory).
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "server.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (a directory containing both server.json and src/) " +
            $"by walking up from {AppContext.BaseDirectory}.");
    }
}
