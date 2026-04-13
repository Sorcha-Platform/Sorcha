// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="ActionExecutionService.TryResolveJsonPointer"/> — the
/// JSON Pointer walker used by the credential claim mapper. Feature 103 US4
/// exposed the need for nested-path resolution when the action payload nests
/// primitive values (e.g. <c>/name/givenName</c> for a PersonName/v1-backed
/// submission).
/// </summary>
public class JsonPointerResolverTests
{
    [Fact]
    public void TryResolve_FlatPath_ReturnsValue()
    {
        var root = new Dictionary<string, object?>
        {
            ["givenName"] = "Alice",
            ["familyName"] = "O'Brien"
        };

        var ok = ActionExecutionService.TryResolveJsonPointer(root, "/givenName", out var v);

        ok.Should().BeTrue();
        v.Should().Be("Alice");
    }

    [Fact]
    public void TryResolve_Rfc6901EscapeSequences_DecodedBeforeLookup()
    {
        // "/a/b" as a raw property name is written as "a~1b" per RFC 6901.
        // The walker must unescape ~1 → / and ~0 → ~ before dictionary lookup.
        var root = new Dictionary<string, object?>
        {
            ["weird/key"] = "slash-value",
            ["tilde~key"] = "tilde-value"
        };

        ActionExecutionService.TryResolveJsonPointer(root, "/weird~1key", out var slashValue).Should().BeTrue();
        slashValue.Should().Be("slash-value");

        ActionExecutionService.TryResolveJsonPointer(root, "/tilde~0key", out var tildeValue).Should().BeTrue();
        tildeValue.Should().Be("tilde-value");
    }

    [Fact]
    public void TryResolve_NestedPath_ReturnsInnerValue()
    {
        var root = new Dictionary<string, object?>
        {
            ["name"] = new Dictionary<string, object?>
            {
                ["givenName"] = "Alice",
                ["middleName"] = "Maeve",
                ["familyName"] = "O'Brien"
            }
        };

        ActionExecutionService.TryResolveJsonPointer(root, "/name/givenName", out var given).Should().BeTrue();
        given.Should().Be("Alice");

        ActionExecutionService.TryResolveJsonPointer(root, "/name/middleName", out var middle).Should().BeTrue();
        middle.Should().Be("Maeve");
    }

    [Fact]
    public void TryResolve_MissingTopLevel_ReturnsFalse()
    {
        var root = new Dictionary<string, object?> { ["foo"] = "bar" };

        var ok = ActionExecutionService.TryResolveJsonPointer(root, "/missing", out var _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_MissingNestedSegment_ReturnsFalse()
    {
        var root = new Dictionary<string, object?>
        {
            ["name"] = new Dictionary<string, object?> { ["givenName"] = "Alice" }
        };

        var ok = ActionExecutionService.TryResolveJsonPointer(root, "/name/missing", out var _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void TryResolve_RootAndEmptyPointer_ReturnFalse()
    {
        // RFC 6901 strictly defines "" as the whole-document pointer and
        // "/" as the single empty-string key. Neither has a use case for
        // claim mapping, so the walker treats both as "no resolution" and
        // returns false. This test pins the simplification so a future
        // change doesn't accidentally start returning the root document.
        var root = new Dictionary<string, object?> { ["x"] = 1 };
        ActionExecutionService.TryResolveJsonPointer(root, "/", out _).Should().BeFalse();
        ActionExecutionService.TryResolveJsonPointer(root, string.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_ReturnsWholeSubtreeOnObjectSourceField()
    {
        // Feature 103 credential mapping: `/address` should return the full
        // address object, not a per-property drill-down.
        var address = new Dictionary<string, object?>
        {
            ["line1"] = "42 Grafton Street",
            ["town"] = "Dublin"
        };
        var root = new Dictionary<string, object?> { ["address"] = address };

        var ok = ActionExecutionService.TryResolveJsonPointer(root, "/address", out var value);

        ok.Should().BeTrue();
        value.Should().BeSameAs(address);
    }

    [Fact]
    public void TryResolve_JsonElementValues_WalksJsonStructure()
    {
        // When the payload was deserialised via System.Text.Json, the nested
        // values are JsonElement instances. The walker must descend through
        // them transparently.
        var doc = JsonDocument.Parse("""
            { "name": { "givenName": "Alice", "middleName": "Maeve" } }
            """);
        var root = new Dictionary<string, object?>
        {
            ["name"] = doc.RootElement.GetProperty("name")
        };

        ActionExecutionService.TryResolveJsonPointer(root, "/name/givenName", out var given).Should().BeTrue();
        ((JsonElement)given!).GetString().Should().Be("Alice");

        ActionExecutionService.TryResolveJsonPointer(root, "/name/middleName", out var middle).Should().BeTrue();
        ((JsonElement)middle!).GetString().Should().Be("Maeve");
    }

    [Fact]
    public void TryResolve_DeeperNesting_WalksAllSegments()
    {
        var root = new Dictionary<string, object?>
        {
            ["a"] = new Dictionary<string, object?>
            {
                ["b"] = new Dictionary<string, object?>
                {
                    ["c"] = "deep"
                }
            }
        };

        ActionExecutionService.TryResolveJsonPointer(root, "/a/b/c", out var value).Should().BeTrue();
        value.Should().Be("deep");
    }
}
