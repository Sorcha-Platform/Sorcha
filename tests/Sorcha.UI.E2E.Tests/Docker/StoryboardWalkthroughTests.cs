// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Drives an authenticated run-through of the platform's key pages against the running Docker
/// stack, capturing a full-page screenshot and the live page state (title / URL / timestamp) for
/// each, then writes a markdown storyboard that embeds the shots. Output directory is taken from
/// the <c>STORYBOARD_DIR</c> environment variable (falls back to a <c>storyboard/</c> folder next
/// to the test binary). Not a pass/fail assertion suite — it is a capture run.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Storyboard")]
public class StoryboardWalkthroughTests : AuthenticatedDockerTestBase
{
    private static readonly (string Route, string Title, string Note)[] Steps =
    [
        (TestConstants.AuthenticatedRoutes.Dashboard,      "Dashboard",            "Landing surface after sign-in — the operator's home."),
        (TestConstants.AuthenticatedRoutes.DesignerBlueprint, "AI Blueprint Designer", "The Describe → Understand → Rehearse → Go-live workflow designer shell."),
        (TestConstants.AuthenticatedRoutes.Blueprints,     "Blueprints",           "Published workflow definitions."),
        (TestConstants.AuthenticatedRoutes.Templates,      "Templates",            "Reusable blueprint templates."),
        (TestConstants.AuthenticatedRoutes.Schemas,        "Schema Library",       "Core + external JSON schemas available to blueprints."),
        (TestConstants.AuthenticatedRoutes.Registers,      "Registers",            "Distributed-ledger registers (the immutable record)."),
        (TestConstants.AuthenticatedRoutes.Wallets,        "Wallets",              "HD wallets / crypto identities."),
        (TestConstants.AuthenticatedRoutes.MyActions,      "My Actions",           "Pending workflow actions awaiting the signed-in participant."),
        (TestConstants.AuthenticatedRoutes.Credentials,    "Credentials",          "Held verifiable credentials."),
        (TestConstants.AuthenticatedRoutes.AdminHealth,    "Admin · Health",       "Service + storage-provider health overview."),
        (TestConstants.AuthenticatedRoutes.AdminOrganizations, "Admin · Organisations", "Tenant orgs (System Admin + auto-seeded Public org)."),
        (TestConstants.AuthenticatedRoutes.Settings,       "Settings",             "User / platform settings."),
    ];

    [Test]
    public async Task Storyboard_CaptureKeyPages()
    {
        var outDir = Environment.GetEnvironmentVariable("STORYBOARD_DIR")
                     ?? Path.Combine(AppContext.BaseDirectory, "storyboard");
        Directory.CreateDirectory(outDir);

        var md = new StringBuilder();
        md.AppendLine("# Sorcha platform — walkthrough storyboard");
        md.AppendLine();
        md.AppendLine($"Captured against the local Docker stack at `{TestConstants.UiWebUrl}` "
                      + $"on {DateTime.Now:yyyy-MM-dd HH:mm} (local).");
        md.AppendLine();
        md.AppendLine($"Signed in as `{TestConstants.TestEmail}`. {Steps.Length} screens captured.");
        md.AppendLine();
        md.AppendLine("> Note: images were built before the M0 verifier fix / M1 AIAS demo merges, "
                      + "so this reflects the platform shell, not those features.");
        md.AppendLine();
        md.AppendLine("---");
        md.AppendLine();

        var step = 0;
        foreach (var (route, title, note) in Steps)
        {
            step++;
            var slug = title.ToLowerInvariant()
                .Replace(" · ", "-").Replace(' ', '-').Replace("/", "");
            var fileName = $"{step:00}-{slug}.png";

            string liveTitle = "(unknown)";
            string url = route;
            string state;
            try
            {
                await NavigateAuthenticatedAsync(route);
                liveTitle = await Page.TitleAsync();
                url = Page.Url;
                await Page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(outDir, fileName),
                    FullPage = true
                });
                state = "captured";
            }
            catch (Exception ex)
            {
                state = $"ERROR: {ex.GetType().Name} — {ex.Message.Split('\n')[0]}";
                TestContext.Out.WriteLine($"[storyboard] {route} failed: {state}");
            }

            md.AppendLine($"## {step}. {title}");
            md.AppendLine();
            md.AppendLine(note);
            md.AppendLine();
            md.AppendLine($"- **Route:** `{route}`");
            md.AppendLine($"- **Resolved URL:** {url}");
            md.AppendLine($"- **Page title:** {liveTitle}");
            md.AppendLine($"- **State:** {state}");
            md.AppendLine($"- **Captured:** {DateTime.Now:HH:mm:ss}");
            md.AppendLine();
            if (state == "captured")
            {
                md.AppendLine($"![{title}]({fileName})");
                md.AppendLine();
            }
            md.AppendLine("---");
            md.AppendLine();
        }

        var docPath = Path.Combine(outDir, "STORYBOARD.md");
        await File.WriteAllTextAsync(docPath, md.ToString());
        TestContext.Out.WriteLine($"[storyboard] written: {docPath}");

        Assert.That(File.Exists(docPath), Is.True, "storyboard doc should be written");
    }
}
