// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Models;

public class OrganizationTests
{
    [Fact]
    public void Organization_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var organization = new Organization();

        // Assert
        organization.Id.Should().NotBeEmpty();
        organization.Name.Should().BeEmpty();
        organization.Subdomain.Should().BeEmpty();
        organization.Status.Should().Be(OrganizationStatus.Active);
        organization.CreatorIdentityId.Should().BeNull();
        organization.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        organization.Branding.Should().BeNull();
    }

    [Fact]
    public void Organization_ShouldAllowSettingAllProperties()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddDays(-10);
        var branding = new BrandingConfiguration
        {
            LogoUrl = "https://example.com/logo.png",
            PrimaryColor = "#0078D4",
            SecondaryColor = "#50E6FF",
            CompanyTagline = "Innovation Through Technology"
        };

        // Act
        var organization = new Organization
        {
            Id = organizationId,
            Name = "Acme Corporation",
            Subdomain = "acme",
            Status = OrganizationStatus.Active,
            CreatorIdentityId = creatorId,
            CreatedAt = createdAt,
            Branding = branding
        };

        // Assert
        organization.Id.Should().Be(organizationId);
        organization.Name.Should().Be("Acme Corporation");
        organization.Subdomain.Should().Be("acme");
        organization.Status.Should().Be(OrganizationStatus.Active);
        organization.CreatorIdentityId.Should().Be(creatorId);
        organization.CreatedAt.Should().Be(createdAt);
        organization.Branding.Should().NotBeNull();
        organization.Branding!.LogoUrl.Should().Be("https://example.com/logo.png");
        organization.Branding.PrimaryColor.Should().Be("#0078D4");
        organization.Branding.SecondaryColor.Should().Be("#50E6FF");
        organization.Branding.CompanyTagline.Should().Be("Innovation Through Technology");
    }

    [Theory]
    [InlineData(OrganizationStatus.Active)]
    [InlineData(OrganizationStatus.Suspended)]
    [InlineData(OrganizationStatus.Deleted)]
    public void Organization_ShouldSupportAllStatusValues(OrganizationStatus status)
    {
        // Arrange & Act
        var organization = new Organization { Status = status };

        // Assert
        organization.Status.Should().Be(status);
    }

    [Fact]
    public void Organization_ShouldAllowNullBranding()
    {
        // Arrange & Act
        var organization = new Organization
        {
            Name = "Test Org",
            Subdomain = "test",
            Branding = null
        };

        // Assert
        organization.Branding.Should().BeNull();
    }

    [Fact]
    public void Organization_ShouldHaveIdAsGuid()
    {
        // Arrange & Act
        var organization = new Organization();

        // Assert
        organization.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void BrandingConfiguration_ShouldAllowAllOptionalProperties()
    {
        // Arrange & Act
        var branding = new BrandingConfiguration
        {
            LogoUrl = "https://example.com/logo.png",
            PrimaryColor = "#FF0000",
            SecondaryColor = "#00FF00",
            CompanyTagline = "We Build Things"
        };

        // Assert
        branding.LogoUrl.Should().Be("https://example.com/logo.png");
        branding.PrimaryColor.Should().Be("#FF0000");
        branding.SecondaryColor.Should().Be("#00FF00");
        branding.CompanyTagline.Should().Be("We Build Things");
    }

    [Fact]
    public void BrandingConfiguration_ShouldAllowNullValues()
    {
        // Arrange & Act
        var branding = new BrandingConfiguration
        {
            LogoUrl = null,
            PrimaryColor = null,
            SecondaryColor = null,
            CompanyTagline = null
        };

        // Assert
        branding.LogoUrl.Should().BeNull();
        branding.PrimaryColor.Should().BeNull();
        branding.SecondaryColor.Should().BeNull();
        branding.CompanyTagline.Should().BeNull();
    }

    [Fact]
    public void Organization_ShouldHaveIdentityProvidersNavigationProperty()
    {
        // Arrange & Act
        var organization = new Organization();

        // Assert
        organization.IdentityProviders.Should().BeEmpty();
    }

    [Fact]
    public void Organization_CreatedAt_ShouldDefaultToUtcNow()
    {
        // Arrange
        var beforeCreation = DateTimeOffset.UtcNow;

        // Act
        var organization = new Organization();

        // Assert
        var afterCreation = DateTimeOffset.UtcNow;
        organization.CreatedAt.Should().BeOnOrAfter(beforeCreation);
        organization.CreatedAt.Should().BeOnOrBefore(afterCreation);
        organization.CreatedAt.Offset.Should().Be(TimeSpan.Zero); // UTC
    }
}
