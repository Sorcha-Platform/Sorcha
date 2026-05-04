// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.ApiGateway.Discoverability;
using Xunit;

namespace Sorcha.ApiGateway.Tests.Discoverability;

/// <summary>
/// Regression tests for WellKnownOpenApiEndpoints.ConvertJsonNode/ConvertJsonValue.
///
/// Spec 117 (AI Discoverability) added these helpers to convert the served OpenAPI
/// document's JsonNode tree into the plain .NET object graph YamlDotNet expects.
///
/// The bug fixed alongside these tests: ConvertJsonValue called value.GetValue&lt;JsonElement&gt;()
/// unconditionally, which throws when the JsonValue was built in code from a CLR primitive
/// (as OpenApiInfoTransformer does for info.x-standards via JsonArray.Add(string)). The
/// /.well-known/openapi.yaml endpoint 500'd in production with that exception until this
/// fix landed. The integration test missed it because Aspire DistributedApplicationTestingBuilder
/// requires Docker, which CI runs without — the test Skipped silently.
/// </summary>
public class WellKnownOpenApiEndpointsTests
{
    [Fact]
    public void ConvertJsonValue_ClrStringBacked_ReturnsString()
    {
        // This is what OpenApiInfoTransformer produces: arr.Add(string).
        var arr = new JsonArray { "BIP32", "ML-DSA (NIST FIPS 204)" };

        var converted = (List<object?>)WellKnownOpenApiEndpoints.ConvertJsonNode(arr)!;

        converted.Should().HaveCount(2);
        converted[0].Should().Be("BIP32");
        converted[1].Should().Be("ML-DSA (NIST FIPS 204)");
    }

    [Fact]
    public void ConvertJsonValue_ClrIntBacked_ReturnsLong()
    {
        var arr = new JsonArray { 42, 100 };

        var converted = (List<object?>)WellKnownOpenApiEndpoints.ConvertJsonNode(arr)!;

        converted[0].Should().Be(42L);
        converted[1].Should().Be(100L);
    }

    [Fact]
    public void ConvertJsonValue_ClrBoolBacked_ReturnsBool()
    {
        var arr = new JsonArray { true, false };

        var converted = (List<object?>)WellKnownOpenApiEndpoints.ConvertJsonNode(arr)!;

        converted[0].Should().Be(true);
        converted[1].Should().Be(false);
    }

    [Fact]
    public void ConvertJsonNode_JsonElementBacked_StillWorks()
    {
        // Round-trip a JSON literal — the resulting JsonValue is JsonElement-backed,
        // which is the path that already worked. Regression guard.
        var node = JsonNode.Parse("""{"openapi":"3.1.0","info":{"title":"X","version":"1"}}""")!;

        var converted = (Dictionary<string, object?>)WellKnownOpenApiEndpoints.ConvertJsonNode(node)!;

        converted["openapi"].Should().Be("3.1.0");
        var info = (Dictionary<string, object?>)converted["info"]!;
        info["title"].Should().Be("X");
        info["version"].Should().Be("1");
    }

    [Fact]
    public void ConvertJsonNode_MixedTree_RealisticOpenApiInfo()
    {
        // Simulates the actual served document shape after OpenApiInfoTransformer runs:
        // a JsonElement-backed root (deserialised from MapOpenApi output) with
        // CLR-string-backed extensions appended via JsonArray.Add(string).
        var root = JsonNode.Parse("""{"openapi":"3.1.0","info":{"title":"Sorcha","version":"1.0.0"}}""")!;
        var info = root["info"]!.AsObject();
        info["x-mcp-server"] = "/.well-known/mcp.json";
        var standards = new JsonArray();
        standards.Add("BIP32");
        standards.Add("HAIP 1.0");
        info["x-standards"] = standards;

        // Should not throw — this is the call the YAML endpoint makes.
        var act = () => WellKnownOpenApiEndpoints.ConvertJsonNode(root);

        act.Should().NotThrow();
        var converted = (Dictionary<string, object?>)act()!;
        var infoOut = (Dictionary<string, object?>)converted["info"]!;
        infoOut["x-mcp-server"].Should().Be("/.well-known/mcp.json");
        var standardsOut = (List<object?>)infoOut["x-standards"]!;
        standardsOut.Should().BeEquivalentTo(new[] { "BIP32", "HAIP 1.0" });
    }

    [Fact]
    public void ConvertJsonNode_NullNode_ReturnsNull()
    {
        WellKnownOpenApiEndpoints.ConvertJsonNode(null).Should().BeNull();
    }
}
