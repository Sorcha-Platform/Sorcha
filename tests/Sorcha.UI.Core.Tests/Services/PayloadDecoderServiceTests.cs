// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

public class PayloadDecoderServiceTests
{
    private readonly PayloadDecoderService _sut = new();

    // ── TrimBase64 ──────────────────────────────────────────────

    [Fact]
    public void TrimBase64_WithTrailingNewline_TrimsCorrectly()
    {
        var result = _sut.TrimBase64("SGVsbG8=\n");

        result.Should().Be("SGVsbG8=");
    }

    [Fact]
    public void TrimBase64_WithLeadingAndTrailingWhitespace_TrimsCorrectly()
    {
        var result = _sut.TrimBase64("  SGVsbG8=  ");

        result.Should().Be("SGVsbG8=");
    }

    [Fact]
    public void TrimBase64_WithCrLf_TrimsCorrectly()
    {
        var result = _sut.TrimBase64("SGVs\r\nbG8=\r\n");

        result.Should().Be("SGVsbG8=");
    }

    [Fact]
    public void TrimBase64_CleanString_ReturnsUnchanged()
    {
        var result = _sut.TrimBase64("SGVsbG8=");

        result.Should().Be("SGVsbG8=");
    }

    [Fact]
    public void TrimBase64_NullInput_ThrowsArgumentNullException()
    {
        var act = () => _sut.TrimBase64(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── DecodeBase64ToUtf8 ──────────────────────────────────────

    [Fact]
    public void DecodeBase64ToUtf8_ValidBase64Json_DecodesCorrectly()
    {
        var json = "{\"name\":\"test\"}";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var result = _sut.DecodeBase64ToUtf8(base64);

        result.Should().Be(json);
    }

    [Fact]
    public void DecodeBase64ToUtf8_ValidBase64Text_DecodesCorrectly()
    {
        var text = "Hello, World!";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        var result = _sut.DecodeBase64ToUtf8(base64);

        result.Should().Be(text);
    }

    [Fact]
    public void DecodeBase64ToUtf8_MalformedBase64_ReturnsNull()
    {
        var result = _sut.DecodeBase64ToUtf8("not-valid-base64!!!");

        result.Should().BeNull();
    }

    [Fact]
    public void DecodeBase64ToUtf8_EmptyString_ReturnsEmpty()
    {
        var result = _sut.DecodeBase64ToUtf8(string.Empty);

        result.Should().BeEmpty();
    }

    // ── TryFormatJson ───────────────────────────────────────────

    [Fact]
    public void TryFormatJson_ValidJson_ReturnsTrueWithPrettyPrint()
    {
        var result = _sut.TryFormatJson("{\"key\":\"value\"}");

        result.IsJson.Should().BeTrue();
        result.PrettyPrinted.Should().Contain("\"key\"");
        result.PrettyPrinted.Should().Contain("\"value\"");
        result.PrettyPrinted.Should().Contain("\n");
    }

    [Fact]
    public void TryFormatJson_InvalidJson_ReturnsFalse()
    {
        var result = _sut.TryFormatJson("{not json at all");

        result.IsJson.Should().BeFalse();
        result.PrettyPrinted.Should().BeNull();
    }

    [Fact]
    public void TryFormatJson_PlainText_ReturnsFalse()
    {
        var result = _sut.TryFormatJson("just some plain text");

        result.IsJson.Should().BeFalse();
        result.PrettyPrinted.Should().BeNull();
    }

    [Fact]
    public void TryFormatJson_NestedJson_FormatsWithIndentation()
    {
        var nested = "{\"outer\":{\"inner\":{\"deep\":42}}}";

        var result = _sut.TryFormatJson(nested);

        result.IsJson.Should().BeTrue();
        result.PrettyPrinted.Should().Contain("  ");
        result.PrettyPrinted.Should().Contain("\"deep\": 42");
    }

    // ── DetectContentType ───────────────────────────────────────

    [Fact]
    public void DetectContentType_DeclaredType_ReturnsDeclared()
    {
        var result = _sut.DetectContentType("image/png", "{\"valid\":\"json\"}");

        result.Should().Be("image/png");
    }

    [Fact]
    public void DetectContentType_JsonContent_ReturnsApplicationJson()
    {
        var result = _sut.DetectContentType(null, "{\"key\":\"value\"}");

        result.Should().Be("application/json");
    }

    [Fact]
    public void DetectContentType_PlainText_ReturnsTextPlain()
    {
        var result = _sut.DetectContentType(null, "just plain text");

        result.Should().Be("text/plain");
    }

    [Fact]
    public void DetectContentType_NullContent_ReturnsOctetStream()
    {
        var result = _sut.DetectContentType(null, null);

        result.Should().Be("application/octet-stream");
    }
}
