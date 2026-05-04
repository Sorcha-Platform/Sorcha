// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;

namespace Sorcha.Gateway.Integration.Tests;

/// <summary>
/// Integration tests for the well-known OpenAPI surface introduced by spec 117 (AI Discoverability).
/// Covers FR-001, FR-002, FR-007, FR-008, FR-009, FR-046, NFR-006, NFR-008, and the CORS check at T032.
/// </summary>
public class OpenApiWellKnownTests : GatewayIntegrationTestBase
{
    private const string WellKnownJsonPath = "/.well-known/openapi.json";
    private const string WellKnownYamlPath = "/.well-known/openapi.yaml";

    [Fact]
    public async Task GET_WellKnownOpenapiJson_Returns200()
    {
        SkipIfInfrastructureUnavailable();

        var response = await GatewayClient!.GetAsync(WellKnownJsonPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        response.Headers.CacheControl?.ToString().Should().Contain("max-age=300");
    }

    [Fact]
    public async Task GET_WellKnownOpenapiYaml_Returns200_WithApplicationYamlContentType()
    {
        SkipIfInfrastructureUnavailable();

        var response = await GatewayClient!.GetAsync(WellKnownYamlPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/yaml",
            "FR-002 pins the content type after analyze finding A1");
        response.Headers.CacheControl?.ToString().Should().Contain("max-age=300");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        body.Should().Contain("openapi:", "YAML body should serialise the OpenAPI document");
        body.Should().Contain("x-standards:",
            "regression: ConvertJsonValue must handle CLR-string-backed JsonValues produced by " +
            "OpenApiInfoTransformer when info.x-standards is configured non-empty");
    }

    [Fact]
    public async Task OpenApiDocument_ContainsXMcpServer()
    {
        SkipIfInfrastructureUnavailable();

        var info = await GetServedInfoBlockAsync();

        info.TryGetProperty("x-mcp-server", out var mcp).Should().BeTrue("FR-008");
        mcp.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenApiDocument_ContainsXStandards()
    {
        SkipIfInfrastructureUnavailable();

        var info = await GetServedInfoBlockAsync();

        info.TryGetProperty("x-standards", out var standards).Should().BeTrue("FR-009");
        standards.ValueKind.Should().Be(JsonValueKind.Array);
        standards.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OpenApiDocument_VersionMatchesAssemblyVersion()
    {
        SkipIfInfrastructureUnavailable();

        var info = await GetServedInfoBlockAsync();
        var servedVersion = info.GetProperty("version").GetString();

        // Resolve via the gateway's referenced assembly the same way ResolveAssemblyInformationalVersion does.
        // Loading the gateway assembly directly from the test process's resolved load context — entry assembly
        // here would be the test runner, not the gateway, so we use the explicit ApiGateway assembly.
        var gatewayAssembly = Assembly.Load("Sorcha.ApiGateway");
        var raw = gatewayAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var plusIdx = raw.IndexOf('+');
        var expected = plusIdx > 0 ? raw[..plusIdx] : raw;

        servedVersion.Should().Be(expected, "FR-046 — single source of truth for the version");
    }

    [Fact]
    public async Task OpenApiDocument_InfoTitleNonEmpty()
    {
        SkipIfInfrastructureUnavailable();

        var info = await GetServedInfoBlockAsync();
        info.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace("FR-007");
    }

    [Fact]
    public async Task OpenApiDocument_InfoContactUrlIsGitHubOrg()
    {
        SkipIfInfrastructureUnavailable();

        var info = await GetServedInfoBlockAsync();
        info.TryGetProperty("contact", out var contact).Should().BeTrue("FR-007");
        var url = contact.GetProperty("url").GetString();
        url.Should().StartWith("https://github.com/", "FR-007");
        url!.ToLowerInvariant().Should().Contain("sorcha");
    }

    [Fact]
    public async Task OpenApiDocument_ExcludesAdminAndIgnoredEndpoints()
    {
        SkipIfInfrastructureUnavailable();

        var doc = await GetServedDocumentAsync();
        if (!doc.RootElement.TryGetProperty("paths", out var paths))
        {
            // No paths is acceptable for an empty surface; the assertion is only meaningful when paths exist.
            return;
        }

        // NFR-008: any endpoint marked .ExcludeFromDescription() or [ApiExplorerSettings(IgnoreApi = true)]
        // must not appear in the served document. The gateway already routes /openapi/aggregated.json and
        // the well-known aliases with .ExcludeFromDescription(); they should not surface here.
        foreach (var pathProp in paths.EnumerateObject())
        {
            pathProp.Name.Should().NotStartWith("/.well-known/",
                "well-known endpoints are aliases and are excluded from the document (NFR-008)");
            pathProp.Name.Should().NotBe("/openapi/aggregated.json",
                "aggregated OpenAPI helper is excluded from the document (NFR-008)");
        }
    }

    [Fact]
    public async Task GET_WellKnownOpenapiJson_AllowsCorsFromAnyOrigin()
    {
        SkipIfInfrastructureUnavailable();

        // T032 — CORS verify. AddSorchaCors() applies AllowAnyOrigin/Method/Header globally; a
        // preflight from any origin should not return a CORS denial. We assert the simple
        // request case (GET) succeeds for a request carrying an Origin header, which is what an
        // AI agent or browser-based crawler would send.
        var request = new HttpRequestMessage(HttpMethod.Get, WellKnownJsonPath);
        request.Headers.Add("Origin", "https://example.com");
        var response = await GatewayClient!.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // CORS middleware echoes Access-Control-Allow-Origin on the response when AllowAnyOrigin is set.
        response.Headers.Should().Contain(h => h.Key.Equals("Access-Control-Allow-Origin", StringComparison.OrdinalIgnoreCase),
            "well-known endpoints must be CORS-open for cross-origin AI agents (FR-001 + AddSorchaCors)");
    }

    private async Task<JsonDocument> GetServedDocumentAsync()
    {
        var response = await GatewayClient!.GetAsync(WellKnownJsonPath);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private async Task<JsonElement> GetServedInfoBlockAsync()
    {
        var doc = await GetServedDocumentAsync();
        doc.RootElement.TryGetProperty("info", out var info).Should().BeTrue();
        return info.Clone();
    }
}
