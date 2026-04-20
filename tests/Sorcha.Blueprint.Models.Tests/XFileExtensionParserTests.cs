// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.Text.Json;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Tests for Feature 107 Assured Identity v1 additions to <see cref="FileSchemaExtension"/>:
/// the optional <c>capture</c> and <c>embedAs</c> hints. Contract:
/// <c>specs/107-assured-identity-v1/contracts/x-file-capture-extension.md</c>.
/// </summary>
public class XFileExtensionParserTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // --- Existing fields still parse (regression guard) ---

    [Fact]
    public void TryParseFromSchema_ExistingFieldsStillParse_WithNewFieldsAbsent()
    {
        var element = Parse("""
            {
              "type": "string",
              "x-file": {
                "accept": ["image/jpeg"],
                "maxSizePerFile": "8MB",
                "maxChunks": 5
              }
            }
            """);

        FileSchemaExtension.TryParseFromSchema(element, out var ext).Should().BeTrue();
        ext!.Accept.Should().BeEquivalentTo(["image/jpeg"]);
        ext.MaxSizePerFile.Should().Be("8MB");
        ext.MaxChunks.Should().Be(5);
        ext.Capture.Should().BeNull();
        ext.EmbedAs.Should().BeNull();
    }

    // --- capture field ---

    [Theory]
    [InlineData("user")]
    [InlineData("environment")]
    public void TryParseFromSchema_CaptureValid_PopulatesCapture(string capture)
    {
        var element = Parse($$"""
            {
              "type": "string",
              "x-file": {
                "accept": ["image/jpeg"],
                "maxSizePerFile": "5MB",
                "capture": "{{capture}}"
              }
            }
            """);

        FileSchemaExtension.TryParseFromSchema(element, out var ext).Should().BeTrue();
        ext!.Capture.Should().Be(capture);
    }

    [Fact]
    public void TryParseFromSchema_CaptureAbsent_LeavesCaptureNull()
    {
        var element = Parse("""
            {
              "type": "string",
              "x-file": {
                "accept": ["application/pdf"],
                "maxSizePerFile": "16MB"
              }
            }
            """);

        FileSchemaExtension.TryParseFromSchema(element, out var ext).Should().BeTrue();
        ext!.Capture.Should().BeNull();
    }

    [Fact]
    public void TryParseFromSchema_CaptureUnknownValue_PreservedAsIs()
    {
        // Parser is permissive; unknown values round-trip so the UI layer
        // can surface a publish-time warning (WARN_BP_REVIEW_001 is for
        // x-review, but the same pattern applies — validation is a layer
        // above the parser).
        var element = Parse("""
            {
              "type": "string",
              "x-file": {
                "accept": ["image/jpeg"],
                "maxSizePerFile": "5MB",
                "capture": "front-only"
              }
            }
            """);

        FileSchemaExtension.TryParseFromSchema(element, out var ext).Should().BeTrue();
        ext!.Capture.Should().Be("front-only");
        FileSchemaExtension.IsValidCapture(ext.Capture).Should().BeFalse();
    }

    // --- embedAs field ---

    [Fact]
    public void TryParseFromSchema_EmbedAsImageTokenJpeg_Populates()
    {
        var element = Parse("""
            {
              "type": "string",
              "x-file": {
                "accept": ["image/jpeg"],
                "maxSizePerFile": "5MB",
                "embedAs": "image-token-jpeg-240x320"
              }
            }
            """);

        FileSchemaExtension.TryParseFromSchema(element, out var ext).Should().BeTrue();
        ext!.EmbedAs.Should().Be("image-token-jpeg-240x320");
    }

    [Fact]
    public void TryParseFromSchema_EmbedAsAbsent_LeavesEmbedAsNull()
    {
        var element = Parse("""
            {
              "type": "string",
              "x-file": {
                "accept": ["application/pdf"],
                "maxSizePerFile": "32MB"
              }
            }
            """);

        FileSchemaExtension.TryParseFromSchema(element, out var ext).Should().BeTrue();
        ext!.EmbedAs.Should().BeNull();
    }

    // --- IsValid* helpers ---

    [Theory]
    [InlineData(null, true)]
    [InlineData("user", true)]
    [InlineData("environment", true)]
    [InlineData("front", false)]
    [InlineData("USER", false)] // case-sensitive
    [InlineData("", false)]
    public void IsValidCapture_ReturnsExpected(string? input, bool expected) =>
        FileSchemaExtension.IsValidCapture(input).Should().Be(expected);

    [Theory]
    [InlineData(null, true)]
    [InlineData("image-token-jpeg-240x320", true)]
    [InlineData("image-token-jpeg-480x640", false)]
    [InlineData("image-token-png-240x320", false)]
    [InlineData("", false)]
    public void IsValidEmbedAs_ReturnsExpected(string? input, bool expected) =>
        FileSchemaExtension.IsValidEmbedAs(input).Should().Be(expected);
}
