// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Tenant.Models;

using Xunit;

namespace Sorcha.Tenant.Models.Tests;

public class OrganizationContextTests
{
    [Fact]
    public void Constructor_WithTypicalOrgContext_SetsAllProperties()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var context = new OrganizationContext
        {
            OrganizationId = orgId,
            Subdomain = "acme",
            OrganizationName = "Acme Corporation",
            UserId = userId,
            UserEmail = "admin@acme.com",
            Roles = ["Administrator", "Member"],
            IsServicePrincipal = false,
            IsPublicIdentity = false,
            ServiceName = null
        };

        context.OrganizationId.Should().Be(orgId);
        context.Subdomain.Should().Be("acme");
        context.OrganizationName.Should().Be("Acme Corporation");
        context.UserId.Should().Be(userId);
        context.UserEmail.Should().Be("admin@acme.com");
        context.Roles.Should().BeEquivalentTo(["Administrator", "Member"]);
        context.IsServicePrincipal.Should().BeFalse();
        context.IsPublicIdentity.Should().BeFalse();
        context.ServiceName.Should().BeNull();
    }

    [Fact]
    public void Constructor_ServicePrincipal_HasServiceNameAndNoUser()
    {
        var context = new OrganizationContext
        {
            IsServicePrincipal = true,
            ServiceName = "Blueprint",
            OrganizationId = null,
            UserId = null,
            UserEmail = null
        };

        context.IsServicePrincipal.Should().BeTrue();
        context.ServiceName.Should().Be("Blueprint");
        context.OrganizationId.Should().BeNull();
        context.UserId.Should().BeNull();
        context.UserEmail.Should().BeNull();
        context.IsPublicIdentity.Should().BeFalse();
    }

    [Fact]
    public void Constructor_PublicIdentity_HasNoOrgOrUser()
    {
        var context = new OrganizationContext
        {
            IsPublicIdentity = true,
            OrganizationId = null,
            Subdomain = null,
            OrganizationName = null,
            UserId = null,
            UserEmail = null
        };

        context.IsPublicIdentity.Should().BeTrue();
        context.OrganizationId.Should().BeNull();
        context.Subdomain.Should().BeNull();
        context.OrganizationName.Should().BeNull();
        context.IsServicePrincipal.Should().BeFalse();
    }

    [Fact]
    public void Constructor_Defaults_HasEmptyRolesAndFalseFlags()
    {
        var context = new OrganizationContext();

        context.OrganizationId.Should().BeNull();
        context.Subdomain.Should().BeNull();
        context.OrganizationName.Should().BeNull();
        context.UserId.Should().BeNull();
        context.UserEmail.Should().BeNull();
        context.Roles.Should().BeEmpty();
        context.IsServicePrincipal.Should().BeFalse();
        context.IsPublicIdentity.Should().BeFalse();
        context.ServiceName.Should().BeNull();
    }
}
