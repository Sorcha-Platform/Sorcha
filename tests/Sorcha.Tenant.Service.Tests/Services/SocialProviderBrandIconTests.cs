// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class SocialProviderBrandIconTests
{
    private static readonly string[] KnownKeys = ["google", "microsoft", "github", "apple"];

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    [InlineData("github")]
    [InlineData("apple")]
    public void For_KnownProviderLowercase_ReturnsSvg(string key)
    {
        var result = SocialProviderBrandIcon.For(key);

        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
    }

    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("GitHub")]
    [InlineData("Apple")]
    public void For_KnownProviderTitleCase_ReturnsSvg(string key)
    {
        var result = SocialProviderBrandIcon.For(key);

        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
    }

    [Theory]
    [InlineData("GOOGLE")]
    [InlineData("MICROSOFT")]
    [InlineData("GITHUB")]
    [InlineData("APPLE")]
    public void For_KnownProviderUppercase_ReturnsSvg(string key)
    {
        var result = SocialProviderBrandIcon.For(key);

        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
    }

    [Theory]
    [InlineData("GoOgLe")]
    [InlineData("MiCrOsOfT")]
    [InlineData("GiThUb")]
    [InlineData("ApPlE")]
    public void For_KnownProviderMixedCase_ReturnsSvg(string key)
    {
        var result = SocialProviderBrandIcon.For(key);

        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
    }

    [Theory]
    [InlineData("google")]
    [InlineData("microsoft")]
    [InlineData("github")]
    [InlineData("apple")]
    public void For_KnownProvider_DoesNotReturnEmpty(string key)
    {
        var result = SocialProviderBrandIcon.For(key);

        result.Value.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("twitter")]
    [InlineData("unknown-provider")]
    [InlineData("xyz")]
    public void For_UnknownKey_ReturnsFallbackSvg(string key)
    {
        var result = SocialProviderBrandIcon.For(key);

        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
        result.Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void For_NullKey_ReturnsFallbackSvgAndDoesNotThrow()
    {
        var act = () => SocialProviderBrandIcon.For(null);

        act.Should().NotThrow();
        var result = act();
        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
    }

    [Fact]
    public void For_EmptyString_ReturnsFallbackSvgAndDoesNotThrow()
    {
        var act = () => SocialProviderBrandIcon.For(string.Empty);

        act.Should().NotThrow();
        var result = act();
        result.Value.Should().StartWith("<svg");
        result.Value.Should().Contain("aria-hidden=\"true\"");
    }

    [Fact]
    public void For_KnownProviders_EachReturnDistinctSvg()
    {
        var results = KnownKeys.Select(k => SocialProviderBrandIcon.For(k).Value).ToList();

        results.Should().OnlyHaveUniqueItems();
    }
}
