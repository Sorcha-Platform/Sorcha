// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Layout;

/// <summary>
/// Guards the narrow-viewport reflow fixes from issues #1275, #1274 and #1281.
/// <para>
/// These are CSS/layout properties that bUnit cannot assert by rendering — jsdom-style layout is not
/// computed, so a wrapping rule or a safe-area inset is invisible to a rendered-markup assertion.
/// The defects are nonetheless real and regressed easily (a banner row losing its wrap silently goes
/// back to wrapping a sentence over nine lines), so these assert the source declarations directly.
/// That is a weaker test than a layout assertion and is chosen deliberately over no test at all.
/// </para>
/// </summary>
public sealed class MobileReflowTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("the tests must be able to locate the repository root");
        return dir!.FullName;
    }

    private static string Read(params string[] relative)
    {
        var path = Path.Combine(RepoRoot(), Path.Combine(relative));
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// #1275: the pair-a-phone nag wrapped its sentence over NINE lines beside its button, because
    /// the row could not wrap and the text had no flex-basis to defend.
    /// </summary>
    [Fact]
    public void PairingNagBanner_RowWraps_AndTheTextDefendsABasis()
    {
        var css = Read("src", "Apps", "Sorcha.UI", "Sorcha.UI.Components.User",
            "Components", "Pairing", "PairingNagBanner.razor.css");

        css.Should().Contain("flex-wrap: wrap", "the action must be able to drop to its own row (#1275)");
        Regex.IsMatch(css, @"\.banner-text\s*\{[^}]*flex:\s*1\s+1\s+\d+px").Should().BeTrue(
            "the text needs a px flex-basis; `auto` is what let it collapse to a two-word column");
    }

    /// <summary>#1275: the autofill summary wrapped over three lines beside Review / Clear all.</summary>
    [Fact]
    public void PersonaFillSummary_RowWraps_AndTheTextDefendsABasis()
    {
        var css = Read("src", "Apps", "Sorcha.UI", "Sorcha.UI.Components.User",
            "Components", "Forms", "PersonaFillSummary.razor.css");

        Regex.IsMatch(css, @"\.persona-fill-summary\s*\{[^}]*flex-wrap:\s*wrap").Should().BeTrue();
        Regex.IsMatch(css, @"\.persona-fill-summary-text\s*\{[^}]*flex:\s*1\s+1\s+\d+px").Should().BeTrue();
    }

    /// <summary>
    /// #1275: My Pending Actions squeezed its heading over three lines and its subtitle over four,
    /// beside the Connected badge and Refresh.
    /// </summary>
    [Fact]
    public void MyActionsHeader_Wraps()
    {
        var page = Read("src", "Apps", "Sorcha.UI", "Sorcha.UI.Web.Client", "Pages", "MyActions.razor");

        page.Should().Contain("Wrap=\"Wrap.Wrap\"",
            "the header controls must drop to their own row rather than compress the heading (#1275)");
    }

    /// <summary>
    /// #1275: `normal` is the DEFAULT priority and must read neutral. Rendering it amber made every
    /// ordinary action look like a warning, and devalued the amber that `high` needs.
    /// </summary>
    [Fact]
    public void NormalPriority_IsNeutral_NotAWarning()
    {
        var page = Read("src", "Apps", "Sorcha.UI", "Sorcha.UI.Web.Client", "Pages", "MyActions.razor");

        page.Should().NotContain("\"normal\" => Color.Warning",
            "the neutral default state must not be styled as a warning (#1275)");
        page.Should().Contain("\"normal\" => Color.Default");
    }

    /// <summary>
    /// #1281: the iOS inbox overlay drew its header into the status-bar strip, colliding with the
    /// clock and putting the close control out of reach with no other way out.
    /// </summary>
    [Fact]
    public void InboxPanel_InsetsForTheStatusBar_AndFitsANarrowViewport()
    {
        var panel = Read("src", "Apps", "Sorcha.UI", "Sorcha.UI.Components.User",
            "Components", "Inbox", "InboxPanel.razor");

        panel.Should().Contain("safe-area-inset-top",
            "the drawer header must sit below the iOS status bar (#1281)");
        panel.Should().NotContain("Width=\"420px\"",
            "a fixed 420px drawer overflows a ~390px phone viewport (#1281)");
        panel.Should().Contain("min(420px, 100vw)");
    }

    /// <summary>
    /// #1281: the trap was that the ONLY dismiss sat in the unreachable strip. A second dismiss
    /// inside the content area is the guarantee the user can always leave.
    /// </summary>
    [Fact]
    public void InboxPanel_HasADismissInsideTheContentArea()
        => Read("src", "Apps", "Sorcha.UI", "Sorcha.UI.Components.User",
                "Components", "Inbox", "InboxPanel.razor")
            .Should().Contain("inbox-close-footer",
                "the header control must never be the only way out of a full-screen overlay (#1281)");

    /// <summary>
    /// #1274: the build badge sat at bottom 3px, inside the floating tab bar's own 14px..70px band,
    /// so the bar's edge, shadow and backdrop blur clipped it.
    /// </summary>
    [Fact]
    public void BuildBadge_SitsClearOfTheFloatingTabBar()
    {
        var css = Read("src", "Apps", "Sorcha.Wallet.Pwa", "wwwroot", "css", "app.css");

        Regex.IsMatch(css, @"\.sorcha-build-badge\s*\{[^}]*bottom:\s*calc\(3px").Should().BeFalse(
            "3px puts the badge inside the tab bar's band (bar occupies 14px..70px) (#1274)");
        Regex.IsMatch(css, @"\.sorcha-build-badge\s*\{[^}]*bottom:\s*calc\(76px").Should().BeTrue();
    }

    /// <summary>
    /// #1274: the storage bar read as solid-full at 0.0%. The value binding was correct all along —
    /// MudProgressLinear at 0.05 emits translateX(-100%). What looked like a fill was the TRACK,
    /// which MudBlazor tints with the bar's own colour.
    /// </summary>
    [Fact]
    public void StorageBar_ForcesANeutralTrack_SoEmptyReadsAsEmpty()
    {
        var page = Read("src", "Apps", "Sorcha.Wallet.Pwa", "Pages", "Settings.razor");

        page.Should().Contain("settings-storage-bar");
        page.Should().Contain("background-color: var(--mud-palette-lines-default",
            "a primary-tinted track on a near-empty bar is indistinguishable from a full one (#1274)");
    }
}
