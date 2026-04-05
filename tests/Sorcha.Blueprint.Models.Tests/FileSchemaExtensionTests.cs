// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.Text.Json;

namespace Sorcha.Blueprint.Models.Tests;

public class FileSchemaExtensionTests
{
    // --- ParseMaxSizeBytes ---

    [Fact]
    public void ParseMaxSizeBytes_ValidMB_ReturnsCorrectBytes()
    {
        // Arrange
        const string input = "16MB";

        // Act
        var result = FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        result.Should().Be(16L * 1024 * 1024);
    }

    [Fact]
    public void ParseMaxSizeBytes_ValidKB_ReturnsCorrectBytes()
    {
        // Arrange
        const string input = "512KB";

        // Act
        var result = FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        result.Should().Be(512L * 1024);
    }

    [Fact]
    public void ParseMaxSizeBytes_CaseInsensitive_Works()
    {
        // Arrange
        const string input = "4mb";

        // Act
        var result = FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        result.Should().Be(4L * 1024 * 1024);
    }

    [Fact]
    public void ParseMaxSizeBytes_DecimalMB_ReturnsCorrectBytes()
    {
        // Arrange
        const string input = "1.5MB";

        // Act
        var result = FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        result.Should().Be((long)(1.5 * 1024 * 1024));
    }

    [Fact]
    public void ParseMaxSizeBytes_InvalidFormat_ThrowsArgumentException()
    {
        // Arrange
        const string input = "abc";

        // Act
        var act = () => FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseMaxSizeBytes_NoSuffix_ThrowsArgumentException()
    {
        // Arrange
        const string input = "1024";

        // Act
        var act = () => FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseMaxSizeBytes_EmptyString_ThrowsArgumentException()
    {
        // Arrange
        const string input = "";

        // Act
        var act = () => FileSchemaExtension.ParseMaxSizeBytes(input);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    // --- TryParseFromSchema ---

    [Fact]
    public void TryParseFromSchema_WithXFile_ReturnsTrue()
    {
        // Arrange
        var json = """
            {
              "type": "string",
              "x-file": {
                "accept": ["image/jpeg"],
                "maxSizePerFile": "8MB",
                "maxChunks": 5
              }
            }
            """;
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = FileSchemaExtension.TryParseFromSchema(element, out var extension);

        // Assert
        result.Should().BeTrue();
        extension.Should().NotBeNull();
    }

    [Fact]
    public void TryParseFromSchema_WithoutXFile_ReturnsFalse()
    {
        // Arrange
        var json = """{"type": "string", "description": "A plain field"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = FileSchemaExtension.TryParseFromSchema(element, out var extension);

        // Assert
        result.Should().BeFalse();
        extension.Should().BeNull();
    }

    [Fact]
    public void TryParseFromSchema_FullXFile_ParsesAllFields()
    {
        // Arrange
        var json = """
            {
              "type": "string",
              "x-file": {
                "accept": ["image/jpeg", "image/png"],
                "maxSizePerFile": "16MB",
                "maxChunks": 4
              }
            }
            """;
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = FileSchemaExtension.TryParseFromSchema(element, out var extension);

        // Assert
        result.Should().BeTrue();
        extension.Should().NotBeNull();
        extension!.Accept.Should().BeEquivalentTo(["image/jpeg", "image/png"]);
        extension.MaxSizePerFile.Should().Be("16MB");
        extension.MaxChunks.Should().Be(4);
    }

    [Fact]
    public void TryParseFromSchema_DefaultMaxChunks_Is10()
    {
        // Arrange
        var json = """
            {
              "type": "string",
              "x-file": {
                "accept": ["application/pdf"],
                "maxSizePerFile": "32MB"
              }
            }
            """;
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = FileSchemaExtension.TryParseFromSchema(element, out var extension);

        // Assert
        result.Should().BeTrue();
        extension.Should().NotBeNull();
        extension!.MaxChunks.Should().Be(FileSchemaExtension.PlatformMaxChunks);
    }
}
