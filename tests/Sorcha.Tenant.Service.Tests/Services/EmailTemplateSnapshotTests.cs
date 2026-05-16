// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Snapshot tests for every email template pair. Each template is rendered against a
/// fixed canonical model and asserted against committed golden fixtures under
/// <c>Fixtures/Emails/</c>. When a template copy change is intentional, set the
/// <c>UPDATE_EMAIL_FIXTURES=1</c> environment variable and re-run the tests — the
/// fixtures on disk are overwritten with the new output and can be reviewed in git.
/// </summary>
public class EmailTemplateSnapshotTests
{
    private static readonly EmailBranding SorchaBranding = new(
        SenderName: "Sorcha",
        LogoUrl: null,
        PrimaryColor: "#2563eb",
        Tagline: "Decentralised registers for secure data flow",
        ReplyTo: "help@sorcha.dev");

    private static readonly EmailBranding AcmeBranding = new(
        SenderName: "Acme Verification Co.",
        LogoUrl: "https://acme.example/logo.png",
        PrimaryColor: "#FF5722",
        Tagline: "Verify with confidence",
        ReplyTo: "help@sorcha.dev");

    private static readonly EmailBranding AcmeDefaultBranding = new(
        SenderName: "Acme Verification Co.",
        LogoUrl: null,
        PrimaryColor: "#2563eb",
        Tagline: null,
        ReplyTo: "help@sorcha.dev");

    public static TheoryData<string, object> Cases => new()
    {
        {
            "verify",
            new VerifyEmailTemplateModel(
                DisplayName: "Stuart Fraser",
                VerifyUrl: "https://sorcha.dev/auth/verify-email?token=FIXTURE_TOKEN",
                ExpiresInHours: 24,
                Branding: SorchaBranding)
        },
        {
            "invite-branded",
            new InviteEmailTemplateModel(
                InviterName: "Admin User",
                OrganizationName: "Acme Verification Co.",
                RoleDisplayName: "Designer",
                AcceptUrl: "https://sorcha.dev/invitations/accept?token=FIXTURE_TOKEN",
                ExpiresInDays: 7,
                Branding: AcmeBranding)
        },
        {
            "invite-default",
            new InviteEmailTemplateModel(
                InviterName: "Admin User",
                OrganizationName: "Acme Verification Co.",
                RoleDisplayName: "Designer",
                AcceptUrl: "https://sorcha.dev/invitations/accept?token=FIXTURE_TOKEN",
                ExpiresInDays: 7,
                Branding: AcmeDefaultBranding)
        },
        {
            "reset",
            new ResetPasswordTemplateModel(
                DisplayName: "Stuart Fraser",
                ResetUrl: "https://sorcha.dev/auth/reset-password?token=FIXTURE_TOKEN",
                ExpiresInMinutes: 60,
                Branding: SorchaBranding)
        },
        {
            "welcome-public",
            new WelcomePublicTemplateModel(
                DisplayName: "Stuart Fraser",
                DashboardUrl: "https://sorcha.dev/dashboard",
                BrowseRegistersUrl: "https://sorcha.dev/registers",
                DemoWorkflowsUrl: "https://sorcha.dev/blueprints",
                DocsUrl: "https://docs.sorcha.dev",
                Branding: SorchaBranding)
        },
        {
            "welcome-invited",
            new WelcomeInvitedTemplateModel(
                DisplayName: "Stuart Fraser",
                OrganizationName: "Acme Verification Co.",
                RoleDisplayName: "Designer",
                DashboardUrl: "https://sorcha.dev/dashboard",
                Branding: AcmeBranding)
        },
        {
            "pairing-resumption",
            new PairingResumptionTemplateModel(
                DisplayName: "Stuart Fraser",
                ResumptionUrl: "https://sorcha.dev/api/auth/pairing-resumption/redeem?token=FIXTURE_TOKEN",
                ExpiresInHours: 24,
                Branding: SorchaBranding)
        },
    };

    /// <summary>
    /// Maps a case name (e.g. "invite-branded") to the actual template name registered
    /// in the renderer (e.g. "invite"). The branded/default split is for fixture
    /// filenames only — the same template renders both.
    /// </summary>
    private static string ResolveTemplateName(string caseName) => caseName switch
    {
        "invite-branded" or "invite-default" => "invite",
        _ => caseName,
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Render_MatchesGoldenFixture_OrRegeneratesWhenRequested(string caseName, object model)
    {
        var renderer = new ScribanEmailTemplateRenderer();
        var templateName = ResolveTemplateName(caseName);
        var (html, text) = renderer.Render(templateName, model);

        var htmlFixturePath = LocateFixture($"{caseName}.html");
        var textFixturePath = LocateFixture($"{caseName}.txt");

        if (ShouldRegenerate())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(htmlFixturePath)!);
            File.WriteAllText(htmlFixturePath, html);
            File.WriteAllText(textFixturePath, text);
            return;
        }

        File.Exists(htmlFixturePath).Should().BeTrue(
            $"fixture {htmlFixturePath} must be committed. Run with UPDATE_EMAIL_FIXTURES=1 to generate.");
        File.Exists(textFixturePath).Should().BeTrue(
            $"fixture {textFixturePath} must be committed. Run with UPDATE_EMAIL_FIXTURES=1 to generate.");

        var expectedHtml = File.ReadAllText(htmlFixturePath);
        var expectedText = File.ReadAllText(textFixturePath);

        // Normalise line endings so Windows vs Unix checkouts don't fight us.
        Normalise(html).Should().Be(Normalise(expectedHtml),
            $"{caseName}.html output should match committed fixture");
        Normalise(text).Should().Be(Normalise(expectedText),
            $"{caseName}.txt output should match committed fixture");
    }

    private static bool ShouldRegenerate() =>
        Environment.GetEnvironmentVariable("UPDATE_EMAIL_FIXTURES") == "1";

    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    /// <summary>
    /// Resolves the absolute on-disk path of a fixture relative to the test project
    /// source tree. Walks up from the test binary's output directory until it finds
    /// the Fixtures/Emails folder.
    /// </summary>
    private static string LocateFixture(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", "Emails", fileName);
            if (Directory.Exists(Path.Combine(dir.FullName, "Fixtures", "Emails")))
                return candidate;

            // tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/<file>
            var testProjectCandidate = Path.Combine(
                dir.FullName, "tests", "Sorcha.Tenant.Service.Tests", "Fixtures", "Emails", fileName);
            if (File.Exists(testProjectCandidate) || Directory.Exists(Path.GetDirectoryName(testProjectCandidate)!))
                return testProjectCandidate;

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails directory.");
    }
}
