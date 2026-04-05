// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Validation;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

public class FileReferenceValidatorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static JsonElement ParseElement(string json) =>
        JsonDocument.Parse(json).RootElement;

    private static JsonElement ValidFileFieldSchema(
        string? accept = null,
        string? maxSizePerFile = null,
        int? maxChunks = null)
    {
        var acceptJson = accept ?? "[\"image/jpeg\",\"image/png\"]";
        var xFileParts = new List<string> { $"\"accept\": {acceptJson}" };
        if (maxSizePerFile is not null)
            xFileParts.Add($"\"maxSizePerFile\": \"{maxSizePerFile}\"");
        if (maxChunks is not null)
            xFileParts.Add($"\"maxChunks\": {maxChunks}");

        return ParseElement($$"""
            {
                "type": "string",
                "format": "file-reference",
                "x-file": { {{string.Join(", ", xFileParts)}} }
            }
            """);
    }

    /// <summary>
    /// Returns a valid 64-char lowercase hex sha256 hash with the required prefix.
    /// </summary>
    private static string ValidHash() =>
        "sha256:" + new string('a', 64);

    private static JsonElement ValidFileReferenceValue(
        string? fileName = null,
        string? contentType = null,
        long? size = null,
        string? hash = null,
        int chunkCount = 1) =>
        ParseElement($$"""
            {
                "fileName": "{{fileName ?? "document.jpg"}}",
                "contentType": "{{contentType ?? "image/jpeg"}}",
                "size": {{size ?? 1048576}},
                "hash": "{{hash ?? ValidHash()}}",
                "salt": "randomsalt123",
                "chunkTransactionIds": [{{string.Join(", ", Enumerable.Range(1, chunkCount).Select(i => $"\"tx-{i:D4}\""))}}]
            }
            """);

    // -------------------------------------------------------------------------
    // ValidateFileFieldSchema — format checks
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateFileFieldSchema_ValidSchema_ReturnsValid()
    {
        // Arrange
        var schema = ValidFileFieldSchema();

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/attachment");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateFileFieldSchema_MissingFormat_ReturnsInvalid()
    {
        // Arrange
        var schema = ParseElement("""
            {
                "type": "string",
                "x-file": { "accept": ["image/jpeg"] }
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("file-reference");
    }

    [Fact]
    public void ValidateFileFieldSchema_WrongFormat_ReturnsInvalid()
    {
        // Arrange
        var schema = ParseElement("""
            {
                "type": "string",
                "format": "email",
                "x-file": { "accept": ["image/jpeg"] }
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/email");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("file-reference");
    }

    [Fact]
    public void ValidateFileFieldSchema_EmptyAccept_ReturnsInvalid()
    {
        // Arrange — accept is an empty array
        var schema = ValidFileFieldSchema(accept: "[]");

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("accept");
    }

    [Fact]
    public void ValidateFileFieldSchema_MaxSizeExceedsPlatform_ReturnsInvalid()
    {
        // Arrange — 50MB exceeds the 40MB platform cap
        var schema = ValidFileFieldSchema(maxSizePerFile: "50MB");

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("50MB");
    }

    [Fact]
    public void ValidateFileFieldSchema_MaxChunksExceedsPlatform_ReturnsInvalid()
    {
        // Arrange — 15 chunks exceeds the PlatformMaxChunks cap of 10
        var schema = ValidFileFieldSchema(maxChunks: 15);

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("maxChunks");
    }

    [Fact]
    public void ValidateFileFieldSchema_NoXFile_ReturnsValid()
    {
        // Arrange — format: file-reference with no x-file extension (x-file is optional)
        var schema = ParseElement("""
            {
                "type": "string",
                "format": "file-reference"
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateFileFieldSchema(schema, "/properties/attachment");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ValidateFileReferenceValue — runtime value checks
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateFileReferenceValue_ValidReference_ReturnsValid()
    {
        // Arrange
        var value = ValidFileReferenceValue();
        var extension = new FileSchemaExtension
        {
            Accept = ["image/jpeg", "image/png"],
            MaxSizePerFile = "10MB",
            MaxChunks = FileSchemaExtension.PlatformMaxChunks
        };

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, extension, "/attachment");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateFileReferenceValue_MissingFileName_ReturnsInvalid()
    {
        // Arrange — omit fileName
        var value = ParseElement($$"""
            {
                "contentType": "image/jpeg",
                "size": 1048576,
                "hash": "{{ValidHash()}}",
                "salt": "salt123",
                "chunkTransactionIds": ["tx-0001"]
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, null, "/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("fileName"));
    }

    [Fact]
    public void ValidateFileReferenceValue_InvalidHashFormat_ReturnsInvalid()
    {
        // Arrange — hash without "sha256:" prefix
        var value = ValidFileReferenceValue(hash: new string('b', 64));

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, null, "/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("sha256:"));
    }

    [Fact]
    public void ValidateFileReferenceValue_TooManyChunks_ReturnsInvalid()
    {
        // Arrange — 11 chunk IDs exceeds the platform maximum of 10
        var value = ValidFileReferenceValue(chunkCount: 11);

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, null, "/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("chunkTransactionIds"));
    }

    [Fact]
    public void ValidateFileReferenceValue_ZeroSize_ReturnsInvalid()
    {
        // Arrange
        var value = ValidFileReferenceValue(size: 0);

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, null, "/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("greater than zero"));
    }

    [Fact]
    public void ValidateFileReferenceValue_ExceedsPlatformMax_ReturnsInvalid()
    {
        // Arrange — 41 MB exceeds the 40 MB platform cap
        var sizeBytes = 41L * 1024 * 1024;
        var value = ValidFileReferenceValue(size: sizeBytes);

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, null, "/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("platform maximum"));
    }

    [Fact]
    public void ValidateFileReferenceValue_WrongMimeType_ReturnsInvalid()
    {
        // Arrange — contentType "application/pdf" is not in the accept list
        var value = ValidFileReferenceValue(contentType: "application/pdf");
        var extension = new FileSchemaExtension
        {
            Accept = ["image/jpeg", "image/png"],
            MaxChunks = FileSchemaExtension.PlatformMaxChunks
        };

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, extension, "/attachment");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("application/pdf"));
    }

    [Fact]
    public void ValidateFileReferenceValue_OptionalNullField_ReturnsValid()
    {
        // Arrange — null/missing value (JsonValueKind.Null) is not an object, but this
        // tests a Null element — the validator should return a descriptive error (not throw).
        // Per the spec the value must be a JSON object; a null signals an optional field
        // that was not provided, so we verify the validator handles it gracefully.
        var value = ParseElement("null");

        // Act
        var result = FileReferenceValidator.ValidateFileReferenceValue(value, null, "/optionalAttachment");

        // Assert — null is not a valid file reference object; expect a clear, non-throwing error
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // ValidateArrayFileField tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateArrayFileField_ValidArraySchema_ReturnsValid()
    {
        // Arrange
        var schema = ParseElement("""
            {
                "type": "array",
                "minItems": 1,
                "maxItems": 5,
                "items": {
                    "type": "string",
                    "format": "file-reference",
                    "x-file": { "accept": ["image/jpeg", "image/png"] }
                }
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateArrayFileField(schema, "/properties/attachments");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateArrayFileField_MinItemsGreaterThanMaxItems_ReturnsInvalid()
    {
        // Arrange — minItems:5 > maxItems:3
        var schema = ParseElement("""
            {
                "type": "array",
                "minItems": 5,
                "maxItems": 3,
                "items": {
                    "type": "string",
                    "format": "file-reference",
                    "x-file": { "accept": ["image/jpeg"] }
                }
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateArrayFileField(schema, "/properties/attachments");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("maxItems"));
    }

    [Fact]
    public void ValidateArrayFileField_MissingItems_ReturnsInvalid()
    {
        // Arrange — array schema without an "items" sub-schema is not a valid file array field
        var schema = ParseElement("""
            {
                "type": "array",
                "minItems": 1,
                "maxItems": 5
            }
            """);

        // Act
        var result = FileReferenceValidator.ValidateArrayFileField(schema, "/properties/attachments");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("items"));
    }
}
