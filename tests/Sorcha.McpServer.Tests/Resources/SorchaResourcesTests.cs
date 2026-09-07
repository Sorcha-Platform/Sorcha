// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.McpServer.Resources;

namespace Sorcha.McpServer.Tests.Resources;

public class SorchaResourcesTests
{
    [Fact]
    public void BlueprintSchema_IsEmbeddedAndIsValidJsonSchema()
    {
        var schema = SorchaResources.GetBlueprintSchema();

        schema.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(schema);
        doc.RootElement.TryGetProperty("$schema", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("assured-identity")]
    [InlineData("encryption-at-rest")]
    [InlineData("ping-pong")]
    public void Example_IsEmbeddedAndParses(string name)
    {
        var example = SorchaResources.GetExample(name);

        example.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(example!);
        doc.RootElement.TryGetProperty("title", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("participants", out _).Should().BeTrue();
    }

    [Fact]
    public void GetExample_UnknownName_ReturnsNull()
    {
        SorchaResources.GetExample("no-such-example").Should().BeNull();
    }

    [Fact]
    public void Glossary_DistinguishesPublicationIdFromExecDefHash()
    {
        // These answer different questions and live in different value spaces (pattern 22).
        // Comparing one against the other yields a plausible-but-wrong answer rather than an
        // error, which is exactly how isPinnedToLatest shipped hard-wired to false.
        var glossary = SorchaResources.GetGlossary();

        glossary.Should().Contain("publicationTxId");
        glossary.Should().Contain("execDefHash");
    }
}
