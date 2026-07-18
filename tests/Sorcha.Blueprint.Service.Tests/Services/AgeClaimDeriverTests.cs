// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class AgeClaimDeriverTests
{
    private static readonly DateOnly Today = new(2026, 7, 18);

    [Theory]
    [InlineData("2000-05-01", true)]   // 26 — clearly over
    [InlineData("2008-07-17", true)]   // turned 18 yesterday
    [InlineData("2008-07-18", true)]   // 18th birthday today — is over
    [InlineData("2008-07-19", false)]  // 18th birthday tomorrow — not yet
    [InlineData("2020-01-01", false)]  // 6 — under
    public void TryDeriveAgeOver_18_ComputesFromDateOfBirth(string dob, bool expected)
    {
        var ok = AgeClaimDeriver.TryDeriveAgeOver(dob, Today, 18, out var isOver);
        ok.Should().BeTrue();
        isOver.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2000-13-40")]
    public void TryDeriveAgeOver_UnparseableDob_ReturnsFalse(string? dob)
    {
        AgeClaimDeriver.TryDeriveAgeOver(dob, Today, 18, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("age_over_18", true, 18)]
    [InlineData("age_over_21", true, 21)]
    [InlineData("age_over_5", true, 5)]
    [InlineData("fullName", false, 0)]
    [InlineData("age_over_", false, 0)]
    [InlineData("ageOver18", false, 0)]
    public void AgeOverClaimThreshold_MatchesPattern(string claim, bool matches, int expected)
    {
        AgeClaimDeriver.AgeOverClaimThreshold(claim, out var t).Should().Be(matches);
        if (matches) t.Should().Be(expected);
    }
}
