// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Integration tests for <c>GET /.well-known/mcp.json</c>. Spec 117 FR-012, FR-013, FR-014,
/// FR-015, FR-016, FR-046, NFR-006.
/// </summary>
public class McpManifestWellKnownTests : GatewayIntegrationTestBase
{
    private const string ManifestPath = "/.well-known/mcp.json";

    [Fact]
    public async Task GET_WellKnownMcpJson_Returns200()
    {
        SkipIfInfrastructureUnavailable();

        var response = await GatewayClient!.GetAsync(ManifestPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        response.Headers.CacheControl?.ToString().Should().Contain("max-age=300");
    }

    [Fact]
    public async Task Manifest_ContainsRequiredFields()
    {
        SkipIfInfrastructureUnavailable();

        var doc = await GetManifestAsync();
        var root = doc.RootElement;

        // FR-013 — every required field present.
        foreach (var field in new[] { "name", "version", "description", "transports", "authentication", "tool_categories", "tool_catalogue_url", "documentation_url" })
        {
            root.TryGetProperty(field, out _).Should().BeTrue($"FR-013 requires '{field}'");
        }

        root.GetProperty("name").GetString().Should().Be("sorcha-mcp");
    }

    [Fact]
    public async Task Manifest_VersionMatchesAssemblyVersion()
    {
        SkipIfInfrastructureUnavailable();

        var doc = await GetManifestAsync();
        var servedVersion = doc.RootElement.GetProperty("version").GetString();

        var gatewayAssembly = Assembly.Load("Sorcha.ApiGateway");
        var raw = gatewayAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var plusIdx = raw.IndexOf('+');
        var expected = plusIdx > 0 ? raw[..plusIdx] : raw;

        servedVersion.Should().Be(expected, "FR-046 — same version source as the OpenAPI document");
    }

    [Fact]
    public async Task Manifest_TransportsIncludeStdioAndHttpSse()
    {
        SkipIfInfrastructureUnavailable();

        var doc = await GetManifestAsync();
        var transports = doc.RootElement.GetProperty("transports");
        transports.ValueKind.Should().Be(JsonValueKind.Array);

        var types = transports.EnumerateArray()
            .Select(t => t.GetProperty("type").GetString())
            .ToList();

        types.Should().Contain("stdio", "FR-014 requires the stdio transport");
        types.Should().Contain("http+sse", "FR-014 requires the http+sse transport");
    }

    [Fact]
    public async Task Manifest_AuthenticationIsJwtBearer()
    {
        SkipIfInfrastructureUnavailable();

        var doc = await GetManifestAsync();
        var auth = doc.RootElement.GetProperty("authentication");

        auth.GetProperty("type").GetString().Should().Be("jwt-bearer", "FR-015");
        auth.GetProperty("issuer").GetString().Should().NotBeNullOrWhiteSpace();
        auth.GetProperty("audience").GetString().Should().NotBeNullOrWhiteSpace();
        auth.GetProperty("acquisition_url").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Manifest_ToolCategoriesCoverAllThreeSlices()
    {
        SkipIfInfrastructureUnavailable();

        var doc = await GetManifestAsync();
        var categories = doc.RootElement.GetProperty("tool_categories");

        foreach (var slice in new[] { "admin", "designer", "participant" })
        {
            categories.TryGetProperty(slice, out var entry).Should().BeTrue($"FR-016 — slice '{slice}' must be present");
            entry.GetProperty("count").GetInt32().Should().BeGreaterThan(0);
            entry.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    private async Task<JsonDocument> GetManifestAsync()
    {
        var response = await GatewayClient!.GetAsync(ManifestPath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
