// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="EmailBrandingResolver"/>: Sorcha defaults, per-org
/// override, per-field fallback semantics, and security validation of
/// org-controlled logo / colour fields.
/// </summary>
public class EmailBrandingResolverTests
{
    private static EmailBrandingResolver MakeResolver(EmailSettings settings)
        => new(Options.Create(settings), NullLogger<EmailBrandingResolver>.Instance);

    private static EmailSettings SorchaDefaults() => new()
    {
        FromName = "Sorcha",
        LogoUrl = "https://sorcha.dev/assets/logo.png",
        PrimaryColor = "#2563eb",
        Tagline = "Decentralised registers for secure data flow",
        ReplyTo = "help@sorcha.dev",
    };

    [Fact]
    public void GetDefault_ReturnsSorchaBrandingFromSettings()
    {
        var resolver = MakeResolver(SorchaDefaults());

        var branding = resolver.GetDefault();

        branding.SenderName.Should().Be("Sorcha");
        branding.LogoUrl.Should().Be("https://sorcha.dev/assets/logo.png");
        branding.PrimaryColor.Should().Be("#2563eb");
        branding.Tagline.Should().Be("Decentralised registers for secure data flow");
        branding.ReplyTo.Should().Be("help@sorcha.dev");
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
        branding.ReplyTo.Should().Be("help@sorcha.dev");
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
        branding.LogoUrl.Should().Be("https://sorcha.dev/assets/logo.png");
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
        branding.LogoUrl.Should().Be("https://sorcha.dev/assets/logo.png");
        branding.PrimaryColor.Should().Be("#2563eb");
        // Org tagline has no Sorcha fallback — if the org has nothing to say, say nothing.
        branding.Tagline.Should().BeNull();
    }

    [Theory]
    [InlineData("http://acme.example/logo.png")]                    // non-https
    [InlineData("javascript:alert(1)")]                              // xss-style scheme
    [InlineData("\" onerror=\"alert(1)")]                            // broken attribute quoting
    [InlineData("not a url")]                                        // malformed
    [InlineData("/relative/logo.png")]                               // non-absolute
    public void GetForOrganization_MaliciousLogoUrl_FallsBackToSorchaDefault(string badLogo)
    {
        // C-3 guardrail: org-controlled LogoUrl is validated before it can reach
        // a template's src attribute. Anything not `https://<absolute>` is dropped
        // in favour of the Sorcha default.
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme",
            Branding = new BrandingConfiguration { LogoUrl = badLogo },
        };

        var branding = resolver.GetForOrganization(org);

        branding.LogoUrl.Should().Be("https://sorcha.dev/assets/logo.png");
    }

    [Theory]
    [InlineData("red")]                                              // named colour
    [InlineData("#GGG")]                                              // non-hex
    [InlineData("#FF5722; background:url(evil)")]                    // CSS injection attempt
    [InlineData("rgb(255,0,0)")]                                     // unsupported colour form
    [InlineData("")]                                                 // empty
    public void GetForOrganization_MaliciousPrimaryColor_FallsBackToSorchaDefault(string badColour)
    {
        // C-3 guardrail: PrimaryColor is interpolated into CSS attribute values.
        // Only hex literals #RGB or #RRGGBB are allowed; everything else falls
        // back to the Sorcha default so an org admin can't inject CSS.
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme",
            Branding = new BrandingConfiguration { PrimaryColor = badColour },
        };

        var branding = resolver.GetForOrganization(org);

        branding.PrimaryColor.Should().Be("#2563eb");
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#FF5722")]
    [InlineData("#abcdef")]
    public void GetForOrganization_ValidHexPrimaryColor_Kept(string goodColour)
    {
        var resolver = MakeResolver(SorchaDefaults());
        var org = new Organization
        {
            Name = "Acme",
            Branding = new BrandingConfiguration { PrimaryColor = goodColour },
        };

        var branding = resolver.GetForOrganization(org);

        branding.PrimaryColor.Should().Be(goodColour);
    }
}
