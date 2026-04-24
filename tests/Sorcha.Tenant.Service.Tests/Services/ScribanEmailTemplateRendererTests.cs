// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ScribanEmailTemplateRenderer"/>: template discovery at
/// construction, missing-template errors, and basic snake_case model binding.
/// Per-template snapshot tests for verify/invite/reset/welcome live in their own
/// user-story test classes.
/// </summary>
public class ScribanEmailTemplateRendererTests
{
    private static EmailBranding SorchaBranding() => new(
        SenderName: "Sorcha",
        LogoUrl: null,
        PrimaryColor: "#2563eb",
        Tagline: null,
        ReplyTo: "help@sorcha.dev");

    [Fact]
    public void Constructor_DiscoversAndParsesEveryEmbeddedTemplatePair()
    {
        // The renderer loads from the Tenant Service assembly at construction.
        // If any template fails to parse, the constructor throws — this test is the
        // canary for authoring errors.
        Action act = () => new ScribanEmailTemplateRenderer();
        act.Should().NotThrow();
    }

    [Fact]
    public void Render_BaseTemplate_WithSorchaBranding_IncludesSenderAndReplyTo()
    {
        var renderer = new ScribanEmailTemplateRenderer();

        // Use the verify template as a proxy for "base + a child" — the base.html
        // is only rendered via {{ include 'base.html' }}. Verify invokes base.
        var model = new VerifyEmailTemplateModel(
            DisplayName: "Stuart Fraser",
            VerifyUrl: "https://sorcha.dev/auth/verify-email?token=FIXTURE_TOKEN",
            ExpiresInHours: 24,
            Branding: SorchaBranding());

        var (html, text) = renderer.Render("verify", model);

        html.Should().Contain("Sorcha");
        html.Should().Contain("help@sorcha.dev");
        text.Should().Contain("Sorcha");
        text.Should().Contain("help@sorcha.dev");
    }

    [Fact]
    public void Render_UnknownTemplateName_ThrowsKeyNotFoundException()
    {
        var renderer = new ScribanEmailTemplateRenderer();
        var model = new VerifyEmailTemplateModel("x", "x", 24, SorchaBranding());

        Action act = () => renderer.Render("does-not-exist", model);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*does-not-exist*");
    }

    [Fact]
    public void Render_VerifyTemplate_BindsSnakeCaseFieldsFromPascalCaseProperties()
    {
        var renderer = new ScribanEmailTemplateRenderer();
        var model = new VerifyEmailTemplateModel(
            DisplayName: "Stuart Fraser",
            VerifyUrl: "https://sorcha.dev/auth/verify-email?token=FIXTURE_TOKEN_ABC123",
            ExpiresInHours: 24,
            Branding: SorchaBranding());

        var (html, text) = renderer.Render("verify", model);

        // DisplayName → display_name appears as the greeting target
        html.Should().Contain("Stuart Fraser");
        text.Should().Contain("Stuart Fraser");

        // VerifyUrl → verify_url appears in both the CTA and plaintext
        html.Should().Contain("FIXTURE_TOKEN_ABC123");
        text.Should().Contain("FIXTURE_TOKEN_ABC123");

        // ExpiresInHours → expires_in_hours is mentioned in the expiry line
        html.Should().Contain("24");
        text.Should().Contain("24");
    }
}
