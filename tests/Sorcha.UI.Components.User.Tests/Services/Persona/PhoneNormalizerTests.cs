// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Globalization;
using FluentAssertions;
using Sorcha.UI.Core.Services.Persona;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Services.Persona;

/// <summary>
/// Unit tests for <see cref="PhoneNormalizer"/> — the shared national → E.164 conversion used by the
/// onboarding profile step and the My Profile editor. Regression for a GB national number
/// ("07966242717") being rejected as invalid_phone instead of saved as "+447966242717".
/// </summary>
public sealed class PhoneNormalizerTests
{
    private static T WithRegion<T>(string culture, Func<T> body)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToE164_Blank_ReturnsNull(string? input) =>
        PhoneNormalizer.ToE164(input).Should().BeNull();

    [Theory]
    [InlineData("+447700900123", "+447700900123")]
    [InlineData("+44 7700 900123", "+447700900123")]   // spaces stripped
    [InlineData("+44 (7700) 900-123", "+447700900123")] // punctuation stripped
    [InlineData("00447700900123", "+447700900123")]     // international access code
    public void ToE164_AlreadyInternational_IsCleanedRegionIndependent(string input, string expected) =>
        PhoneNormalizer.ToE164(input).Should().Be(expected);

    [Fact]
    public void ToE164_GbNationalNumber_PromotedToPlus44()
    {
        // The reported case: a GB national number typed on a GB browser.
        WithRegion("en-GB", () => PhoneNormalizer.ToE164("07966242717"))
            .Should().Be("+447966242717");
    }

    [Fact]
    public void ToE164_GbNationalWithSpaces_PromotedToPlus44()
    {
        WithRegion("en-GB", () => PhoneNormalizer.ToE164("07966 242717"))
            .Should().Be("+447966242717");
    }

    [Fact]
    public void ToE164_FrenchNationalOnFrenchBrowser_PromotedToPlus33()
    {
        WithRegion("fr-FR", () => PhoneNormalizer.ToE164("0612345678"))
            .Should().Be("+33612345678");
    }

    [Fact]
    public void ToE164_UnmappedRegion_LeavesNationalNumberUntouched()
    {
        // Japan isn't in the common-region map: never invent a country code.
        WithRegion("ja-JP", () => PhoneNormalizer.ToE164("09012345678"))
            .Should().Be("09012345678");
    }

    [Theory]
    [InlineData("phones[0].value", "invalid_phone")]
    public void DescribeError_InvalidPhone_IsHumanReadable(string field, string code)
    {
        var message = PhoneNormalizer.DescribeError(field, code);
        message.Should().Contain("international format");
        message.Should().NotContain("phones[0]");
    }
}
