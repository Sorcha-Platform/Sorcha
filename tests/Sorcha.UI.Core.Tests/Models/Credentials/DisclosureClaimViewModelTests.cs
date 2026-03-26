// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Models.Credentials;

/// <summary>
/// Tests for DisclosureClaimViewModel.CategoriseClaims static method.
/// </summary>
public class DisclosureClaimViewModelTests
{
    private static readonly Dictionary<string, object> AllClaims = new()
    {
        { "name", "Alice" },
        { "dob", "1990-01-01" },
        { "email", "alice@example.com" },
        { "phone", "+1234567890" },
        { "address", "123 Main St" },
    };

    [Fact]
    public void CategoriseClaims_RequiredClaim_HasCategoryRequired()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name", "email" };
        var optional = new[] { "dob" };

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        var nameClaim = result.Single(c => c.ClaimName == "name");
        nameClaim.Category.Should().Be(DisclosureCategory.Required);
    }

    [Fact]
    public void CategoriseClaims_RequiredClaim_HasIsSharingTrue()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name" };
        var optional = Array.Empty<string>();

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        result.Single(c => c.ClaimName == "name").IsSharing.Should().BeTrue();
    }

    [Fact]
    public void CategoriseClaims_OptionalClaim_HasCategoryOptional()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name", "email" };
        var optional = new[] { "email" };

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        result.Single(c => c.ClaimName == "email").Category.Should().Be(DisclosureCategory.Optional);
    }

    [Fact]
    public void CategoriseClaims_OptionalClaim_DefaultsIsSharingFalse()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name", "email" };
        var optional = new[] { "email" };

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        result.Single(c => c.ClaimName == "email").IsSharing.Should().BeFalse();
    }

    [Fact]
    public void CategoriseClaims_ClaimNotInAnySet_HasCategoryNotRequested()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name" };
        var optional = Array.Empty<string>();

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        var dob = result.Single(c => c.ClaimName == "dob");
        dob.Category.Should().Be(DisclosureCategory.NotRequested);
    }

    [Fact]
    public void CategoriseClaims_OrdersRequiredFirst_ThenOptional_ThenNotRequested()
    {
        var required = new[] { "phone" };
        var disclosable = new[] { "phone", "email" };
        var optional = new[] { "email" };

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        var categories = result.Select(c => c.Category).ToList();
        var firstRequired = categories.IndexOf(DisclosureCategory.Required);
        var firstOptional = categories.IndexOf(DisclosureCategory.Optional);
        var firstNotRequested = categories.IndexOf(DisclosureCategory.NotRequested);

        firstRequired.Should().BeLessThan(firstOptional);
        firstOptional.Should().BeLessThan(firstNotRequested);
    }

    [Fact]
    public void CategoriseClaims_AllClaimsPresent_AllClaimsIncluded()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name", "email" };
        var optional = new[] { "dob" };

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        result.Should().HaveCount(AllClaims.Count);
        result.Select(c => c.ClaimName).Should().BeEquivalentTo(AllClaims.Keys);
    }

    [Fact]
    public void CategoriseClaims_SetsClaimValue_FromAllClaims()
    {
        var required = new[] { "name" };
        var disclosable = new[] { "name" };
        var optional = Array.Empty<string>();

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        result.Single(c => c.ClaimName == "name").ClaimValue.Should().Be(AllClaims["name"]);
    }

    [Fact]
    public void CategoriseClaims_EmptyAllClaims_ReturnsEmpty()
    {
        var result = DisclosureClaimViewModel.CategoriseClaims(
            new Dictionary<string, object>(),
            new[] { "name" },
            new[] { "name" },
            Array.Empty<string>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void CategoriseClaims_ClaimInBothRequiredAndOptional_TreatedAsRequired()
    {
        // Required takes precedence over optional
        var required = new[] { "name" };
        var disclosable = new[] { "name" };
        var optional = new[] { "name" };

        var result = DisclosureClaimViewModel.CategoriseClaims(AllClaims, required, disclosable, optional);

        result.Single(c => c.ClaimName == "name").Category.Should().Be(DisclosureCategory.Required);
    }
}
