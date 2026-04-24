// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="EmailBrandingResolver"/>: Sorcha defaults, per-org
/// override, and per-field fallback semantics.
/// </summary>
public class EmailBrandingResolverTests
{
    private static EmailBrandingResolver MakeResolver(EmailSettings settings)
        => new(Options.Create(settings));

    private static EmailSettings SorchaDefaults() => new()
    {
        FromName = "Sorcha",
        LogoUrl = "https://sorcha.io/assets/logo.png",
        PrimaryColor = "#2563eb",
        Tagline = "Decentralised registers for secure data flow",
        ReplyTo = "help@sorcha.io",
    };

    [Fact]
    public void GetDefault_ReturnsSorchaBrandingFromSettings()
    {
        var resolver = MakeResolver(SorchaDefaults());

        var branding = resolver.GetDefault();

        branding.SenderName.Should().Be("Sorcha");
        branding.LogoUrl.Should().Be("https://sorcha.io/assets/logo.png");
        branding.PrimaryColor.Should().Be("#2563eb");
        branding.Tagline.Should().Be("Decentralised registers for secure data flow");
        branding.ReplyTo.Should().Be("help@sorcha.io");
    }

    [Fact]
    public void GetForOrganization_FullyBrandedOrg_UsesOrgValuesExceptReplyTo()
    {
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme Verification Co.",
            Branding = new BrandingConfiguration
            {
                LogoUrl = "https://acme.example/logo.png",
                PrimaryColor = "#FF5722",
                CompanyTagline = "Verify with confidence",
            },
        };

        var branding = resolver.GetForOrganization(org);

        branding.SenderName.Should().Be("Acme Verification Co.");
        branding.LogoUrl.Should().Be("https://acme.example/logo.png");
        branding.PrimaryColor.Should().Be("#FF5722");
        branding.Tagline.Should().Be("Verify with confidence");
        // Reply-to is platform-level even for org-branded messages.
        branding.ReplyTo.Should().Be("help@sorcha.io");
    }

    [Fact]
    public void GetForOrganization_MissingLogo_FallsBackToSorchaLogo()
    {
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme",
            Branding = new BrandingConfiguration
            {
                LogoUrl = null,
                PrimaryColor = "#FF5722",
            },
        };

        var branding = resolver.GetForOrganization(org);

        branding.SenderName.Should().Be("Acme");
        branding.LogoUrl.Should().Be("https://sorcha.io/assets/logo.png");
        branding.PrimaryColor.Should().Be("#FF5722");
    }

    [Fact]
    public void GetForOrganization_MissingColor_FallsBackToSorchaColor()
    {
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme",
            Branding = new BrandingConfiguration
            {
                LogoUrl = "https://acme.example/logo.png",
                PrimaryColor = null,
            },
        };

        var branding = resolver.GetForOrganization(org);

        branding.PrimaryColor.Should().Be("#2563eb");
    }

    [Fact]
    public void GetForOrganization_NullBranding_UsesOrgNameWithSorchaDefaults()
    {
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme",
            Branding = null,
        };

        var branding = resolver.GetForOrganization(org);

        branding.SenderName.Should().Be("Acme");
        branding.LogoUrl.Should().Be("https://sorcha.io/assets/logo.png");
        branding.PrimaryColor.Should().Be("#2563eb");
        // Org tagline has no Sorcha fallback — if the org has nothing to say, say nothing.
        branding.Tagline.Should().BeNull();
    }
}
