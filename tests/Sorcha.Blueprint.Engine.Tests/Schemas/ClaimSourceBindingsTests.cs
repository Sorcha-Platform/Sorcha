// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Schemas;
using Xunit;

namespace Sorcha.Blueprint.Engine.Tests.Schemas;

/// <summary>
/// Covers the pure discovery and coercion rules behind server-side <c>x-claim-source</c> resolution
/// (issue #1264). The original client-side seeder made a binding only as fresh as the browser's token,
/// which auto-rejected a compliant citizen; these rules now run at submission on the server, where the
/// live value is knowable.
/// </summary>
public sealed class ClaimSourceBindingsTests
{
    private static JsonDocument Schema(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Discover_FindsABinding_WithItsDeclaredType()
    {
        var schema = Schema("""
            {
              "type": "object",
              "properties": {
                "emailVerified": { "type": "boolean", "x-claim-source": "email_verified" }
              }
            }
            """);

        var bindings = ClaimSourceBindings.Discover(new[] { schema });

        bindings.Should().ContainSingle();
        bindings[0].PropertyName.Should().Be("emailVerified");
        bindings[0].ClaimName.Should().Be("email_verified");
        bindings[0].DeclaredType.Should().Be("boolean");
    }

    [Fact]
    public void Discover_IgnoresPropertiesWithoutTheKeyword()
    {
        var schema = Schema("""
            {
              "properties": {
                "name": { "type": "string" },
                "emailVerified": { "type": "boolean", "x-claim-source": "email_verified" }
              }
            }
            """);

        ClaimSourceBindings.Discover(new[] { schema }).Should().ContainSingle()
            .Which.PropertyName.Should().Be("emailVerified");
    }

    /// <summary>An action carries one schema per form page, so every page must be walked.</summary>
    [Fact]
    public void Discover_WalksEverySchemaInTheAction()
    {
        var page1 = Schema("""{"properties":{"a":{"type":"boolean","x-claim-source":"email_verified"}}}""");
        var page2 = Schema("""{"properties":{"b":{"type":"string","x-claim-source":"email"}}}""");

        var bindings = ClaimSourceBindings.Discover(new[] { page1, page2 });

        bindings.Should().HaveCount(2);
        bindings.Should().Contain(b => b.PropertyName == "a" && b.ClaimName == "email_verified");
        bindings.Should().Contain(b => b.PropertyName == "b" && b.ClaimName == "email");
    }

    /// <summary>
    /// A property declared on two pages must resolve once. Reporting it twice would mean two writes
    /// where the last silently decides the value.
    /// </summary>
    [Fact]
    public void Discover_DeduplicatesAPropertyDeclaredTwice()
    {
        var page1 = Schema("""{"properties":{"emailVerified":{"type":"boolean","x-claim-source":"email_verified"}}}""");
        var page2 = Schema("""{"properties":{"emailVerified":{"type":"boolean","x-claim-source":"email"}}}""");

        var bindings = ClaimSourceBindings.Discover(new[] { page1, page2 });

        bindings.Should().ContainSingle().Which.ClaimName.Should().Be(
            "email_verified", "the first declaration wins, deterministically");
    }

    [Theory]
    [InlineData("""{"properties":{"x":{"type":"boolean","x-claim-source":""}}}""")]      // empty name
    [InlineData("""{"properties":{"x":{"type":"boolean","x-claim-source":true}}}""")]    // wrong node type
    [InlineData("""{"properties":{"x":"not-an-object"}}""")]                             // property not an object
    [InlineData("""{"type":"object"}""")]                                                // no properties
    [InlineData("""[]""")]                                                               // not an object at all
    public void Discover_SkipsMalformedDeclarations_WithoutThrowing(string json)
    {
        var act = () => ClaimSourceBindings.Discover(new[] { Schema(json) });

        act.Should().NotThrow("a blueprint author's typo must not take down submission");
        ClaimSourceBindings.Discover(new[] { Schema(json) }).Should().BeEmpty();
    }

    [Fact]
    public void Discover_NullSchemas_IsEmpty()
        => ClaimSourceBindings.Discover(null).Should().BeEmpty();

    [Fact]
    public void ClaimNames_AreDistinct()
    {
        var bindings = new List<ClaimSourceBinding>
        {
            new("a", "email_verified", "boolean"),
            new("b", "email_verified", "boolean"),
            new("c", "email", "string"),
        };

        ClaimSourceBindings.ClaimNames(bindings).Should().BeEquivalentTo(new[] { "email_verified", "email" });
    }

    // ---- Coercion: the fail-closed rules that decide an identity gate ----

    [Fact]
    public void Coerce_BooleanTrue_IsTrue()
        => ClaimSourceBindings.Coerce(new("v", "email_verified", "boolean"), "true").Should().Be(true);

    /// <summary>
    /// The #1264 gate must stay real: anything that is not an explicit "true" denies. Absent and
    /// unparseable both deny, and a boolean binding NEVER removes the property — an identity gate has
    /// to land on a definite deny rather than an absent field whose meaning depends on the consumer.
    /// </summary>
    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData(null)]
    public void Coerce_BooleanAnythingElse_IsFalse_NeverRemoved(string? claimValue)
    {
        var result = ClaimSourceBindings.Coerce(new("v", "email_verified", "boolean"), claimValue);

        result.Should().Be(false);
        result.Should().NotBeNull("removing the property would make the gate's meaning consumer-dependent");
    }

    [Fact]
    public void Coerce_BooleanIsCaseInsensitiveOnTrue()
        => ClaimSourceBindings.Coerce(new("v", "email_verified", "boolean"), "True").Should().Be(true);

    [Fact]
    public void Coerce_StringBinding_PassesTheValueThrough()
        => ClaimSourceBindings.Coerce(new("e", "email", "string"), "live@example.test")
            .Should().Be("live@example.test");

    /// <summary>
    /// An unresolved non-boolean binding removes the property, so a client-asserted or stale value can
    /// never survive in place of one the server declined to vouch for.
    /// </summary>
    [Fact]
    public void Coerce_UnresolvedStringBinding_RemovesTheProperty()
        => ClaimSourceBindings.Coerce(new("e", "email", "string"), null).Should().BeNull();
}
