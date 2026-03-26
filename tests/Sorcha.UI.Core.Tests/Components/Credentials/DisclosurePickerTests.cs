// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Credentials;

/// <summary>
/// Unit tests for the disclosure-picker logic encapsulated in
/// <see cref="DisclosureClaimViewModel"/>.
/// </summary>
public class DisclosurePickerTests
{
    // -------------------------------------------------------------------------
    // GetDisclosedClaimNames
    // -------------------------------------------------------------------------

    [Fact]
    public void GetDisclosedClaimNames_RequiredClaims_AlwaysReturnsAllRequired()
    {
        // Arrange
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name", ClaimValue = "Alice", Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "family_name", ClaimValue = "Smith", Category = DisclosureCategory.Required, IsSharing = false }
        };

        // Act
        var disclosed = DisclosureClaimViewModel.GetDisclosedClaimNames(claims);

        // Assert
        disclosed.Should().BeEquivalentTo(["given_name", "family_name"]);
    }

    [Fact]
    public void GetDisclosedClaimNames_OptionalClaimWithIsSharingTrue_IncludesOptionalClaim()
    {
        // Arrange
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name", ClaimValue = "Alice", Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "email", ClaimValue = "alice@example.com", Category = DisclosureCategory.Optional, IsSharing = true }
        };

        // Act
        var disclosed = DisclosureClaimViewModel.GetDisclosedClaimNames(claims);

        // Assert
        disclosed.Should().Contain("email");
        disclosed.Should().Contain("given_name");
    }

    [Fact]
    public void GetDisclosedClaimNames_OptionalClaimWithIsSharingFalse_ExcludesOptionalClaim()
    {
        // Arrange
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name", ClaimValue = "Alice", Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "email", ClaimValue = "alice@example.com", Category = DisclosureCategory.Optional, IsSharing = false }
        };

        // Act
        var disclosed = DisclosureClaimViewModel.GetDisclosedClaimNames(claims);

        // Assert
        disclosed.Should().NotContain("email");
        disclosed.Should().Contain("given_name");
    }

    [Fact]
    public void GetDisclosedClaimNames_NotRequestedClaims_NeverIncluded()
    {
        // Arrange
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name", ClaimValue = "Alice", Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "secret_field", ClaimValue = "hidden", Category = DisclosureCategory.NotRequested, IsSharing = true }
        };

        // Act
        var disclosed = DisclosureClaimViewModel.GetDisclosedClaimNames(claims);

        // Assert — even with IsSharing=true, NotRequested claims must never appear
        disclosed.Should().NotContain("secret_field");
    }

    [Fact]
    public void GetDisclosedClaimNames_AllCategories_OnlyRequiredAndSharingOptionalReturned()
    {
        // Arrange
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name",   ClaimValue = "Alice",             Category = DisclosureCategory.Required,     IsSharing = false },
            new() { ClaimName = "family_name",  ClaimValue = "Smith",             Category = DisclosureCategory.Required,     IsSharing = false },
            new() { ClaimName = "email",        ClaimValue = "alice@example.com", Category = DisclosureCategory.Optional,     IsSharing = true  },
            new() { ClaimName = "phone",        ClaimValue = "555-1234",          Category = DisclosureCategory.Optional,     IsSharing = false },
            new() { ClaimName = "internal_id",  ClaimValue = "XYZ-001",           Category = DisclosureCategory.NotRequested, IsSharing = false },
            new() { ClaimName = "secret",       ClaimValue = "hidden",            Category = DisclosureCategory.NotRequested, IsSharing = true  }
        };

        // Act
        var disclosed = DisclosureClaimViewModel.GetDisclosedClaimNames(claims);

        // Assert
        disclosed.Should().BeEquivalentTo(["given_name", "family_name", "email"]);
    }

    [Fact]
    public void GetDisclosedClaimNames_EmptyList_ReturnsEmpty()
    {
        var disclosed = DisclosureClaimViewModel.GetDisclosedClaimNames([]);
        disclosed.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // GetSharingSummary
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSharingSummary_TwoRequiredOneOptionalSharing_ReturnsCorrectSummary()
    {
        // Arrange — 2 required + 1 optional sharing = 3 of 3 requested claims
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name",  ClaimValue = "Alice",             Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "family_name", ClaimValue = "Smith",             Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "email",       ClaimValue = "alice@example.com", Category = DisclosureCategory.Optional, IsSharing = true  }
        };

        // Act
        var summary = DisclosureClaimViewModel.GetSharingSummary(claims);

        // Assert
        summary.Should().Be("Sharing 3 of 3 claims");
    }

    [Fact]
    public void GetSharingSummary_OptionalNotShared_ExcludedFromNumerator()
    {
        // Arrange — 1 required, 1 optional not sharing = sharing 1 of 2
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name", ClaimValue = "Bob",              Category = DisclosureCategory.Required, IsSharing = false },
            new() { ClaimName = "email",      ClaimValue = "bob@example.com",  Category = DisclosureCategory.Optional, IsSharing = false }
        };

        // Act
        var summary = DisclosureClaimViewModel.GetSharingSummary(claims);

        // Assert
        summary.Should().Be("Sharing 1 of 2 claims");
    }

    [Fact]
    public void GetSharingSummary_NotRequestedClaimsExcludedFromTotal()
    {
        // Arrange — 2 required, 1 not-requested = "Sharing 2 of 2 claims" (not 2 of 3)
        var claims = new List<DisclosureClaimViewModel>
        {
            new() { ClaimName = "given_name",  ClaimValue = "Alice",   Category = DisclosureCategory.Required,     IsSharing = false },
            new() { ClaimName = "family_name", ClaimValue = "Smith",   Category = DisclosureCategory.Required,     IsSharing = false },
            new() { ClaimName = "internal_id", ClaimValue = "XYZ-001", Category = DisclosureCategory.NotRequested, IsSharing = false }
        };

        // Act
        var summary = DisclosureClaimViewModel.GetSharingSummary(claims);

        // Assert
        summary.Should().Be("Sharing 2 of 2 claims");
    }

    // -------------------------------------------------------------------------
    // CategoriseClaims factory
    // -------------------------------------------------------------------------

    [Fact]
    public void CategoriseClaims_RequiredAndOptionalAndExtra_CategorisesCorrectly()
    {
        // Arrange
        var all = new Dictionary<string, object>
        {
            ["given_name"]  = "Alice",
            ["email"]       = "alice@example.com",
            ["internal_id"] = "XYZ-001"
        };

        // Act
        var result = DisclosureClaimViewModel.CategoriseClaims(
            all,
            requiredClaims: ["given_name"],
            optionalClaims: ["email"]);

        // Assert
        result.Should().HaveCount(3);
        result.Single(c => c.ClaimName == "given_name").Category.Should().Be(DisclosureCategory.Required);
        result.Single(c => c.ClaimName == "email").Category.Should().Be(DisclosureCategory.Optional);
        result.Single(c => c.ClaimName == "internal_id").Category.Should().Be(DisclosureCategory.NotRequested);
    }

    [Fact]
    public void CategoriseClaims_AllClaims_DefaultIsSharingFalse()
    {
        // Arrange
        var all = new Dictionary<string, object> { ["given_name"] = "Bob", ["email"] = "bob@example.com" };

        // Act
        var result = DisclosureClaimViewModel.CategoriseClaims(all, ["given_name"], ["email"]);

        // Assert — required claims are IsSharing=true; optional claims default to IsSharing=false (opt-in)
        result.Single(c => c.ClaimName == "given_name").IsSharing.Should().BeTrue();
        result.Single(c => c.ClaimName == "email").IsSharing.Should().BeFalse();
    }
}
