// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.Forms;
using System.Text.Json;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Tests for <see cref="DateTimeFieldBoundResolver"/> — the static helper that
/// the <c>DateTimeRenderer.razor</c> uses to resolve <c>formatMinimum</c> /
/// <c>formatMaximum</c> Sorcha date tokens into concrete <see cref="DateOnly"/>
/// bounds for the date-picker. Feature 107 Assured Identity v1 — DOB future-block.
/// </summary>
public class DateTimeRendererFutureBlockTests
{
    private static readonly DateOnly FixedToday = new(2026, 4, 20);

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    // --- formatMaximum: "today" (DOB future-block) ---

    [Fact]
    public void TryResolveBound_FormatMaximumToday_ReturnsToday()
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMaximum": "today" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMaximum", FixedToday, out var bound)
            .Should().BeTrue();
        bound.Should().Be(FixedToday);
    }

    // --- formatMaximum: "today-18Y" (age-gate) ---

    [Fact]
    public void TryResolveBound_FormatMaximumTodayMinus18Y_ReturnsDateEighteenYearsAgo()
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMaximum": "today-18Y" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMaximum", FixedToday, out var bound)
            .Should().BeTrue();
        bound.Should().Be(FixedToday.AddYears(-18));
    }

    // --- formatMinimum: "today" (future-only gate) ---

    [Fact]
    public void TryResolveBound_FormatMinimumToday_ReturnsToday()
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMinimum": "today" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMinimum", FixedToday, out var bound)
            .Should().BeTrue();
        bound.Should().Be(FixedToday);
    }

    [Fact]
    public void TryResolveBound_FormatMinimumTodayPlus30D_ReturnsDateThirtyDaysAhead()
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMinimum": "today+30D" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMinimum", FixedToday, out var bound)
            .Should().BeTrue();
        bound.Should().Be(FixedToday.AddDays(30));
    }

    // --- ISO-8601 literal passes through ---

    [Fact]
    public void TryResolveBound_IsoLiteralBound_ParsesAsAbsoluteDate()
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMaximum": "2000-01-01" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMaximum", FixedToday, out var bound)
            .Should().BeTrue();
        bound.Should().Be(new DateOnly(2000, 1, 1));
    }

    // --- Absent bound → no constraint ---

    [Fact]
    public void TryResolveBound_BoundAbsent_ReturnsFalseAndNull()
    {
        var schema = Schema("""{ "type": "string", "format": "date" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMaximum", FixedToday, out var bound)
            .Should().BeFalse();
        bound.Should().BeNull();
    }

    [Fact]
    public void TryResolveBound_BothBoundsAbsent_NeitherResolves()
    {
        var schema = Schema("""{ "type": "string", "format": "date" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMinimum", FixedToday, out var minBound)
            .Should().BeFalse();
        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMaximum", FixedToday, out var maxBound)
            .Should().BeFalse();
        minBound.Should().BeNull();
        maxBound.Should().BeNull();
    }

    // --- Unparseable token → false, caller falls back to unbounded picker ---

    [Fact]
    public void TryResolveBound_UnparseableToken_ReturnsFalse()
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMaximum": "whenever" }""");

        DateTimeFieldBoundResolver.TryResolveBound(schema, "formatMaximum", FixedToday, out var bound)
            .Should().BeFalse();
        bound.Should().BeNull();
    }

    // --- Caller-side typo → throws (Release WASM strips Debug.Assert; #339) ---

    [Theory]
    [InlineData("formatMin")]
    [InlineData("FormatMaximum")]
    [InlineData("")]
    public void TryResolveBound_UnknownBoundName_ThrowsArgumentException(string boundName)
    {
        var schema = Schema("""{ "type": "string", "format": "date", "formatMaximum": "today" }""");

        var act = () => DateTimeFieldBoundResolver.TryResolveBound(schema, boundName, FixedToday, out _);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Unknown boundName '{boundName}'*")
            .And.ParamName.Should().Be("boundName");
    }
}
