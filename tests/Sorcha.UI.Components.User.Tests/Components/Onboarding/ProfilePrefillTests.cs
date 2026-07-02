// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using System.Security.Claims;
using FluentAssertions;
using Sorcha.UI.Components.User.Components.Onboarding;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Components.Onboarding;

/// <summary>
/// Unit tests for <see cref="ProfilePrefill"/> — the signup → persona carry-across logic behind the
/// CompleteProfileStep onboarding component. Regression coverage for the bug where a new user's name
/// and email were not taken across into their profile (the email was dropped and the display name was
/// never split into given/family).
/// </summary>
public sealed class ProfilePrefillTests
{
    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "jwt",
            nameType: "name",
            roleType: "role");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void FromClaims_DisplayNameAndEmail_CarriesBothAcross()
    {
        // The reported bug: signing up gave a display name + email, but neither reached the persona.
        var user = PrincipalWith(("name", "Alice Smith"), ("email", "alice@example.com"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.GivenName.Should().Be("Alice");
        seed.FamilyName.Should().Be("Smith");
        seed.FullName.Should().Be("Alice Smith");
        seed.Email.Should().Be("alice@example.com", "the login email must be carried across (this was previously dropped)");
        seed.HasData.Should().BeTrue();
    }

    [Fact]
    public void FromClaims_SingleTokenName_SetsGivenNameOnly()
    {
        var user = PrincipalWith(("name", "Prince"), ("email", "prince@example.com"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.GivenName.Should().Be("Prince");
        seed.FamilyName.Should().BeNull();
        seed.FullName.Should().Be("Prince");
    }

    [Fact]
    public void FromClaims_MultiPartName_TreatsRemainderAsFamilyName()
    {
        var user = PrincipalWith(("name", "Ada Mary Lovelace"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.GivenName.Should().Be("Ada");
        seed.FamilyName.Should().Be("Mary Lovelace");
        seed.FullName.Should().Be("Ada Mary Lovelace");
    }

    [Fact]
    public void FromClaims_GranularSocialClaims_PreferredOverSplittingDisplayName()
    {
        // A social provider that supplies given_name/family_name separately must be trusted over a
        // naive split of the display name (e.g. compound family names the split would get wrong).
        var user = PrincipalWith(
            ("given_name", "Maria"),
            ("family_name", "van der Berg"),
            ("name", "Maria van der Berg"),
            ("email", "maria@example.com"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.GivenName.Should().Be("Maria");
        seed.FamilyName.Should().Be("van der Berg");
        seed.FullName.Should().Be("Maria van der Berg");
        seed.Email.Should().Be("maria@example.com");
    }

    [Fact]
    public void FromClaims_PhoneNumberClaim_CarriedAcross()
    {
        var user = PrincipalWith(("name", "Bob Jones"), ("phone_number", "+447700900123"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.Phone.Should().Be("+447700900123");
    }

    [Fact]
    public void FromClaims_PreferredUsernameFallback_UsedWhenNameAbsent()
    {
        var user = PrincipalWith(("preferred_username", "Carol Danvers"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.GivenName.Should().Be("Carol");
        seed.FamilyName.Should().Be("Danvers");
    }

    [Fact]
    public void FromClaims_StandardClaimTypeEmail_UsedWhenShortNameAbsent()
    {
        // Some hosts surface the email under the long ClaimTypes.Email URI rather than "email".
        var user = PrincipalWith((ClaimTypes.Email, "dana@example.com"), ("name", "Dana Scully"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.Email.Should().Be("dana@example.com");
    }

    [Fact]
    public void FromClaims_NoIdentityClaims_HasNoData()
    {
        var user = PrincipalWith(("sub", "abc-123"), ("role", "user"));

        var seed = ProfilePrefill.FromClaims(user);

        seed.HasData.Should().BeFalse();
        seed.GivenName.Should().BeNull();
        seed.Email.Should().BeNull();
    }

    [Theory]
    [InlineData("  Grace   Hopper  ", "Grace", "Hopper")]
    [InlineData("Grace", "Grace", null)]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    public void SplitDisplayName_Cases(string input, string? expectedGiven, string? expectedFamily)
    {
        var (given, family) = ProfilePrefill.SplitDisplayName(input);

        given.Should().Be(expectedGiven);
        family.Should().Be(expectedFamily);
    }
}
