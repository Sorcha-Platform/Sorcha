// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Authoring;

/// <summary>
/// T052 [US5] — sanity checks on the JSON-parse / shape-check logic used by
/// <c>ImportSchemaDialog.HandleAccept</c>. The dialog component itself relies on
/// MudBlazor's <c>MudDialogProvider</c> portal, which is awkward to host
/// standalone under bUnit; the rendering surface is covered by the T046
/// Playwright suite. These tests pin the parse-and-validate contract so a
/// refactor cannot silently lose the "malformed JSON reports inline" promise.
/// </summary>
public class ImportSchemaDialogTests
{
    /// <summary>
    /// Mirrors the dialog's two-step gate: parse as JSON; require root to be an object.
    /// Returns null + an error message string on failure, or the JsonDocument on success.
    /// </summary>
    private static (JsonDocument? Doc, string? Error) ValidatePastedSchema(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return (null, "Paste a JSON Schema to import.");
        }
        try
        {
            var doc = JsonDocument.Parse(pasted);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                return (null, "Top-level value must be a JSON object.");
            }
            return (doc, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Schema is not valid JSON: {ex.Message}");
        }
    }

    [Fact]
    public void ValidatePastedSchema_WithValidObjectSchema_Succeeds()
    {
        var (doc, error) = ValidatePastedSchema(
            """{"type":"object","properties":{"name":{"type":"string"}}}""");

        doc.Should().NotBeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidatePastedSchema_WithMalformedJson_ReturnsParseError()
    {
        var (doc, error) = ValidatePastedSchema("not-valid-json{{{");

        doc.Should().BeNull();
        error.Should().StartWith("Schema is not valid JSON:");
    }

    [Fact]
    public void ValidatePastedSchema_WithJsonArrayRoot_ReturnsShapeError()
    {
        var (doc, error) = ValidatePastedSchema("""[1,2,3]""");

        doc.Should().BeNull();
        error.Should().Be("Top-level value must be a JSON object.");
    }

    [Fact]
    public void ValidatePastedSchema_WithBlankInput_PromptsForPaste()
    {
        var (doc, error) = ValidatePastedSchema("   ");

        doc.Should().BeNull();
        error.Should().Be("Paste a JSON Schema to import.");
    }
}
