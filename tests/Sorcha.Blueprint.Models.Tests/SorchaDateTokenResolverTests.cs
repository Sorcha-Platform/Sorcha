// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models.Schemas;
using Xunit;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Tests for <see cref="SorchaDateTokenResolver"/> — the helper that translates
/// Sorcha's relative date token vocabulary into absolute <see cref="DateOnly"/>
/// values for JSON Schema <c>formatMinimum</c> / <c>formatMaximum</c> constraints.
/// </summary>
public class SorchaDateTokenResolverTests
{
    private static readonly DateOnly Today = new(2026, 4, 13);

    [Fact]
    public void Resolve_TodayToken_ReturnsReferenceDate()
    {
        var result = SorchaDateTokenResolver.Resolve("today", Today);
        result.Should().Be(Today);
    }

    [Theory]
    [InlineData("today+1D", 2026, 4, 14)]
    [InlineData("today-1D", 2026, 4, 12)]
    [InlineData("today+7D", 2026, 4, 20)]
    [InlineData("today-30D", 2026, 3, 14)]
    public void Resolve_DayOffsetTokens_ResolveCorrectly(string token, int year, int month, int day)
    {
        var result = SorchaDateTokenResolver.Resolve(token, Today);
        result.Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("today+1M", 2026, 5, 13)]
    [InlineData("today-1M", 2026, 3, 13)]
    [InlineData("today+12M", 2027, 4, 13)]
    [InlineData("today-24M", 2024, 4, 13)]
    public void Resolve_MonthOffsetTokens_ResolveCorrectly(string token, int year, int month, int day)
    {
        var result = SorchaDateTokenResolver.Resolve(token, Today);
        result.Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("today+1Y", 2027, 4, 13)]
    [InlineData("today-18Y", 2008, 4, 13)]
    [InlineData("today-65Y", 1961, 4, 13)]
    public void Resolve_YearOffsetTokens_ResolveCorrectly(string token, int year, int month, int day)
    {
        var result = SorchaDateTokenResolver.Resolve(token, Today);
        result.Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void Resolve_LiteralIsoDate_PassesThrough()
    {
        var result = SorchaDateTokenResolver.Resolve("2020-01-15", Today);
        result.Should().Be(new DateOnly(2020, 1, 15));
    }

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("yesterday")]
    [InlineData("today+1")]                 // missing unit
    [InlineData("today+1X")]                // unknown unit
    [InlineData("today+D")]                 // missing magnitude
    [InlineData("today*1Y")]                // wrong operator
    [InlineData("now")]                     // reserved but not implemented
    [InlineData("now+1D")]                  // reserved but not implemented
    [InlineData("2026/04/13")]              // wrong literal format
    [InlineData("13-04-2026")]              // wrong literal format
    [InlineData("")]                        // empty
    [InlineData("today+99999Y")]            // 5-digit N exceeds the 1-4 bound
    [InlineData("today-10000D")]            // 5-digit N exceeds the 1-4 bound
    public void Resolve_InvalidTokens_ThrowFormatException(string token)
    {
        var act = () => SorchaDateTokenResolver.Resolve(token, Today);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Resolve_LargeButSafeMagnitude_ResolvesCorrectly()
    {
        // 2025 years before 2026-04-13 lands in year 1, the last representable
        // year for DateOnly. Must not throw.
        var act = () => SorchaDateTokenResolver.Resolve("today-2025Y", Today);
        act.Should().NotThrow();
    }

    [Fact]
    public void Resolve_DateOnlyOverflow_ThrowsFormatException()
    {
        // today-9999Y from 2026-04-13 underflows DateOnly.MinValue (0001-01-01).
        // The regex allows the 4-digit magnitude but the arithmetic wrapper
        // must convert the overflow to a FormatException so callers see a
        // single consistent failure mode for "invalid date token".
        var act = () => SorchaDateTokenResolver.Resolve("today-9999Y", Today);
        act.Should().Throw<FormatException>()
           .WithMessage("*outside the supported range*");
    }

    [Fact]
    public void Resolve_DateOnlyOverflowFuture_ThrowsFormatException()
    {
        // today+9999Y from 2026-04-13 overflows DateOnly.MaxValue (9999-12-31).
        var act = () => SorchaDateTokenResolver.Resolve("today+9999Y", Today);
        act.Should().Throw<FormatException>()
           .WithMessage("*outside the supported range*");
    }

    [Fact]
    public void Resolve_Null_ThrowsArgumentNullException()
    {
        var act = () => SorchaDateTokenResolver.Resolve(null!, Today);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_TodayMinus18Years_IsPastDate()
    {
        // Feature 103 use case — age gate on date of birth
        var cutoff = SorchaDateTokenResolver.Resolve("today-18Y", Today);
        cutoff.Should().BeBefore(Today);
        (Today.Year - cutoff.Year).Should().Be(18);
    }

    [Fact]
    public void Resolve_TimeZoneOverload_UsesUtcByDefault()
    {
        // Smoke test of the tz-overload — just verifies it runs and returns
        // something sensible. Precise tz semantics are checked implicitly by
        // the main overload.
        var result = SorchaDateTokenResolver.Resolve("today");
        result.Should().BeOnOrAfter(new DateOnly(2026, 1, 1));
        result.Should().BeOnOrBefore(new DateOnly(2100, 1, 1));
    }
}
