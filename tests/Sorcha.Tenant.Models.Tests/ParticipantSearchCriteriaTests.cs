// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Tenant.Models;

using Xunit;

namespace Sorcha.Tenant.Models.Tests;

public class ParticipantSearchCriteriaTests
{
    [Fact]
    public void Constructor_Defaults_HasCorrectPagination()
    {
        var criteria = new ParticipantSearchCriteria();

        criteria.Page.Should().Be(1);
        criteria.PageSize.Should().Be(20);
        criteria.Query.Should().BeNull();
        criteria.OrganizationId.Should().BeNull();
        criteria.Status.Should().BeNull();
        criteria.HasLinkedWallet.Should().BeNull();
        criteria.AccessibleOrganizations.Should().BeNull();
        criteria.IsSystemAdmin.Should().BeFalse();
    }

    [Fact]
    public void Equality_TwoInstancesWithSameValues_AreEqual()
    {
        var orgId = Guid.NewGuid();
        var accessibleOrgs = new List<Guid> { Guid.NewGuid() };

        var criteria1 = new ParticipantSearchCriteria
        {
            Query = "test",
            OrganizationId = orgId,
            Status = ParticipantIdentityStatus.Active,
            HasLinkedWallet = true,
            Page = 2,
            PageSize = 10,
            AccessibleOrganizations = accessibleOrgs,
            IsSystemAdmin = false
        };

        var criteria2 = new ParticipantSearchCriteria
        {
            Query = "test",
            OrganizationId = orgId,
            Status = ParticipantIdentityStatus.Active,
            HasLinkedWallet = true,
            Page = 2,
            PageSize = 10,
            AccessibleOrganizations = accessibleOrgs,
            IsSystemAdmin = false
        };

        criteria1.Should().Be(criteria2);
    }

    [Fact]
    public void WithExpression_CreatesNewInstanceWithModifiedProperty()
    {
        var original = new ParticipantSearchCriteria
        {
            Query = "test",
            Page = 1,
            PageSize = 20
        };

        var modified = original with { Page = 3 };

        modified.Page.Should().Be(3);
        modified.Query.Should().Be("test");
        modified.PageSize.Should().Be(20);
        original.Page.Should().Be(1, "original should be unmodified");
    }
}
