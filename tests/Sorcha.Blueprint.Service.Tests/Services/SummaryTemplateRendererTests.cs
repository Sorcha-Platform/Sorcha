// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for SummaryTemplateRenderer token replacement and fallback behaviour.
/// </summary>
public class SummaryTemplateRendererTests
{
    private static JsonElement Parse(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Render_ValidTemplate_ReplacesTokens()
    {
        var payload = Parse("""{"orderRef": "4421", "item": "Widget"}""");
        var result = SummaryTemplateRenderer.Render(
            "Order #{{payload.orderRef}} — {{payload.item}}",
            payload,
            "fallback");

        result.Should().Be("Order #4421 — Widget");
    }

    [Fact]
    public void Render_MissingField_ReturnsFallback()
    {
        var payload = Parse("""{"other": "value"}""");
        var result = SummaryTemplateRenderer.Render(
            "{{payload.missing}}",
            payload,
            "default summary");

        result.Should().Be("default summary");
    }

    [Fact]
    public void Render_NestedPath_ResolvesCorrectly()
    {
        var payload = Parse("""{"order": {"ref": "ABC-123"}}""");
        var result = SummaryTemplateRenderer.Render(
            "Ref: {{payload.order.ref}}",
            payload,
            "fallback");

        result.Should().Be("Ref: ABC-123");
    }

    [Fact]
    public void Render_NullTemplate_ReturnsFallback()
    {
        var payload = Parse("""{"field": "value"}""");
        var result = SummaryTemplateRenderer.Render(null, payload, "default");

        result.Should().Be("default");
    }

    [Fact]
    public void Render_EmptyPayload_ReturnsFallback()
    {
        var payload = Parse("{}");
        var result = SummaryTemplateRenderer.Render(
            "{{payload.field}}",
            payload,
            "default");

        result.Should().Be("default");
    }

    [Fact]
    public void Render_MultipleTokens_AllResolved()
    {
        var payload = Parse("""{"a": "1", "b": "2", "c": "3"}""");
        var result = SummaryTemplateRenderer.Render(
            "A={{payload.a}}, B={{payload.b}}, C={{payload.c}}",
            payload,
            "fallback");

        result.Should().Be("A=1, B=2, C=3");
    }

    [Fact]
    public void Render_NullPayload_ReturnsFallback()
    {
        var result = SummaryTemplateRenderer.Render(
            "{{payload.field}}",
            null,
            "default");

        result.Should().Be("default");
    }

    [Fact]
    public void Render_NumericValue_RendersAsString()
    {
        var payload = Parse("""{"count": 42}""");
        var result = SummaryTemplateRenderer.Render(
            "Count: {{payload.count}}",
            payload,
            "fallback");

        result.Should().Be("Count: 42");
    }

    [Fact]
    public void Render_MixedResolvedAndUnresolved_ReturnsPartialRender()
    {
        var payload = Parse("""{"known": "value"}""");
        var result = SummaryTemplateRenderer.Render(
            "{{payload.known}} and {{payload.unknown}}",
            payload,
            "fallback");

        result.Should().Be("value and {{payload.unknown}}");
    }
}
