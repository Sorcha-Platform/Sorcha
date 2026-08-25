// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Canonical;

namespace Sorcha.Blueprint.Models.Tests.Canonical;

/// <summary>
/// The canonical form (Feature 195, contracts/publication-identity.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The organising rule, and the reason this class is short: <b>only what survives a JSON parse can
/// vary</b>. Whitespace and string escaping do not — <c>&</c> and <c>&amp;</c> parse to the same
/// string — so no rule is needed for them and the producer's encoder cannot affect the identity.
/// What does survive is object key order, number representation and duplicate-key resolution, and
/// those are what this class pins.
/// </para>
/// <para>
/// This is why neither <c>RegisterSerializationOptions.Canonical</c> nor the former
/// <c>BlueprintContentHash</c> could be reused: both re-serialize a parsed document while
/// <b>preserving input key order</b>, which makes them addresses of the serializer's output rather
/// than of the content.
/// </para>
/// </remarks>
public class BlueprintCanonicalJsonTests
{
    [Fact]
    public void Canonicalise_SortsObjectKeys_Recursively()
    {
        const string input = """
            {"b":1,"a":{"z":true,"y":{"n":2,"m":3}},"c":[{"q":1,"p":2}]}
            """;

        var canonical = BlueprintCanonicalJson.Canonicalise(input);

        canonical.Should().Be("""{"a":{"y":{"m":3,"n":2},"z":true},"b":1,"c":[{"p":2,"q":1}]}""",
            "sorting must be recursive — a nested object left in input order is a serializer-output " +
            "address hiding inside a content address");
    }

    [Fact]
    public void Canonicalise_PreservesArrayOrder()
    {
        const string input = """{"actions":[{"id":2},{"id":0},{"id":1}]}""";

        var canonical = BlueprintCanonicalJson.Canonicalise(input);

        canonical.Should().Be("""{"actions":[{"id":2},{"id":0},{"id":1}]}""",
            "arrays are ordered data, not sets — a blueprint's action and route order is meaning, " +
            "and sorting it would silently rewrite the workflow");
    }

    [Theory]
    [InlineData("{ \"a\" : 1 ,  \"b\" : 2 }")]
    [InlineData("{\n  \"a\": 1,\n  \"b\": 2\n}")]
    [InlineData("{\"a\":1,\"b\":2}")]
    public void Canonicalise_NormalisesWhitespace_BecauseItDoesNotSurviveAParse(string input)
    {
        BlueprintCanonicalJson.Canonicalise(input).Should().Be("""{"a":1,"b":2}""");
    }

    [Fact]
    public void Canonicalise_NormalisesStringEscaping_BecauseItDoesNotSurviveAParse()
    {
        // Two spellings of one document: a literal ampersand, and the JSON & escape for it.
        // The escape is built here rather than in an [InlineData] so there is no doubt about which
        // characters reach the parser.
        var literal = "{\"t\":\"a&b\"}";
        var escaped = "{\"t\":\"a" + "\\u0026" + "b\"}";

        escaped.Should().NotBe(literal, "the two inputs must genuinely differ, or this proves nothing");

        // They parse to the same string, so the PRODUCER's encoder cannot affect the identity. This
        // is the finding that retired an earlier (wrong) claim that the two publish paths' encoders
        // diverged in a way that mattered — and it is why canonicalisation is defined as
        // parse-then-serialize rather than as a set of serializer options.
        BlueprintCanonicalJson.Canonicalise(escaped)
            .Should().Be(BlueprintCanonicalJson.Canonicalise(literal));
    }

    [Fact]
    public void Canonicalise_WritesCharactersRatherThanEscapes()
    {
        var canonical = BlueprintCanonicalJson.Canonicalise("""{"t":"a&b<c>d","x":"é"}""");

        canonical.Should().Contain("a&b<c>d").And.Contain("é",
            "minimal escaping (RFC 8785's intent) — the choice is arbitrary but must be FIXED, " +
            "because it is part of the bytes every definition's identity is computed over");
    }

    [Fact]
    public void Canonicalise_RejectsDuplicateKeys()
    {
        const string input = """{"id":"first","id":"second"}""";

        var act = () => BlueprintCanonicalJson.Canonicalise(input);

        act.Should().Throw<InvalidOperationException>(
            "last-wins is a silent choice about which of two definitions was published. A document " +
            "that cannot be read unambiguously must be refused, not resolved");
    }

    [Fact]
    public void Canonicalise_RejectsDuplicateKeys_Nested()
    {
        const string input = """{"a":{"x":1,"x":2}}""";

        var act = () => BlueprintCanonicalJson.Canonicalise(input);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Canonicalise_RejectsMalformedJson()
    {
        var act = () => BlueprintCanonicalJson.Canonicalise("{not json");

        act.Should().Throw<JsonException>();
    }

    /// <summary>
    /// Pins the number rule settled in T008. Numbers <b>do</b> survive a parse — System.Text.Json
    /// writes a parsed number from its raw text — so <c>1</c>, <c>1.0</c> and <c>1e0</c> are three
    /// different canonical forms unless a rule says otherwise.
    /// </summary>
    [Fact]
    public void Canonicalise_PreservesNumberRepresentation_AndThatIsTheDecision()
    {
        BlueprintCanonicalJson.Canonicalise("""{"n":1}""").Should().Be("""{"n":1}""");
        BlueprintCanonicalJson.Canonicalise("""{"n":1.0}""").Should().Be("""{"n":1.0}""");

        // Deliberately NOT asserted equal to each other. Blueprint numbers originate from a typed
        // model (int / decimal properties), so the producer emits one form consistently; normalising
        // would add a rule with no failure mode to prevent. Recorded as a decision so that a future
        // change to it is a change to this test, not a silent re-identification of every definition.
    }

    [Fact]
    public void Canonicalise_IsIdempotent()
    {
        var once = BlueprintCanonicalJson.Canonicalise(CanonicalTestPaths.GoldenBlueprintJson());
        var twice = BlueprintCanonicalJson.Canonicalise(once);

        twice.Should().Be(once,
            "the canonical form of a canonical form is itself, or the identity is not a fixed point " +
            "and a round trip through any store could re-identify a definition");
    }

    [Fact]
    public void Canonicalise_RejectsNullOrWhitespace()
    {
        FluentActions.Invoking(() => BlueprintCanonicalJson.Canonicalise(null!))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BlueprintCanonicalJson.Canonicalise("   "))
            .Should().Throw<ArgumentException>();
    }
}
