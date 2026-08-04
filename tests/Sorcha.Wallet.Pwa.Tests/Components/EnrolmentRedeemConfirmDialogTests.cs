// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Bunit;

using FluentAssertions;

using Sorcha.Wallet.Pwa.Components;

using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Components;

/// <summary>
/// Feature 126 — the friend-scans-by-mistake confirmation shown after a pairing QR is scanned.
/// </summary>
/// <remarks>
/// <para>
/// This component is deliberately plain HTML rather than MudBlazor components, so it depends
/// entirely on its own scoped stylesheet. That stylesheet <b>did not exist</b>: every
/// <c>enrol-confirm*</c> class in the markup was undefined anywhere in the repository, so on a real
/// phone the screen rendered as a bare heading, two bare paragraphs, and two unstyled buttons
/// running together as one line — "Yes, that's me — continue This isn't me".
/// </para>
/// <para>
/// The rendering tests below cannot catch that on their own: bUnit asserts the DOM, and the DOM was
/// always correct. <see cref="EveryBespokeClassInTheMarkupIsDefinedInTheScopedStylesheet"/> is the
/// test that would have caught it, and it is the reason this file exists.
/// </para>
/// </remarks>
public sealed class EnrolmentRedeemConfirmDialogTests : BunitContext
{
    private const string Email = "citizen@example.test";

    private IRenderedComponent<EnrolmentRedeemConfirmDialog> RenderDialog(
        string? displayName = "Test Citizen") =>
        Render<EnrolmentRedeemConfirmDialog>(p => p
            .Add(c => c.Email, Email)
            .Add(c => c.DisplayName, displayName));

    // ---------------------------------------------------------------------------------------
    // The guard that matters
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EveryBespokeClassInTheMarkupIsDefinedInTheScopedStylesheet()
    {
        var componentDir = LocateComponentDirectory();
        var markup = File.ReadAllText(Path.Combine(componentDir, "EnrolmentRedeemConfirmDialog.razor"));
        var stylesheetPath = Path.Combine(componentDir, "EnrolmentRedeemConfirmDialog.razor.css");

        File.Exists(stylesheetPath).Should().BeTrue(
            "the component styles itself with bespoke classes rather than MudBlazor components, so " +
            "without this stylesheet it renders completely unstyled on a real device");

        var stylesheet = File.ReadAllText(stylesheetPath);

        var used = Regex.Matches(markup, @"class=""(?<classes>[^""]+)""")
            .SelectMany(m => m.Groups["classes"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.StartsWith("enrol-confirm", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        used.Should().NotBeEmpty("the markup should carry the bespoke classes this test guards");

        var undefined = used.Where(c => !stylesheet.Contains($".{c}", StringComparison.Ordinal)).ToList();

        undefined.Should().BeEmpty(
            "a class used in the markup but defined in no stylesheet renders as nothing at all — " +
            "which is exactly how this screen shipped unstyled");
    }

    // ---------------------------------------------------------------------------------------
    // Recognition content — the point of the screen
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ShowsTheBoundAccountSoAnUnintendedRecipientCanRecogniseItIsNotTheirs()
    {
        var dialog = RenderDialog();

        dialog.Markup.Should().Contain(Email);
        dialog.Markup.Should().Contain("Test Citizen");
    }

    [Fact]
    public void WithNoDisplayName_StillShowsTheEmail()
    {
        var dialog = RenderDialog(displayName: null);

        dialog.Markup.Should().Contain(Email);
    }

    [Fact]
    public void OffersBothAConfirmAndACancel()
    {
        // Cancel is the safe action for the person this screen protects, so it must always be
        // present — not merely a way to dismiss.
        var dialog = RenderDialog();

        dialog.FindAll("[data-testid='enrol-confirm-yes']").Should().HaveCount(1);
        dialog.FindAll("[data-testid='enrol-confirm-no']").Should().HaveCount(1);
    }

    [Fact]
    public async Task ConfirmAndCancelRaiseTheirCallbacks()
    {
        var confirmed = false;
        var cancelled = false;

        var dialog = Render<EnrolmentRedeemConfirmDialog>(p => p
            .Add(c => c.Email, Email)
            .Add(c => c.OnConfirm, () => confirmed = true)
            .Add(c => c.OnCancel, () => cancelled = true));

        await dialog.Find("[data-testid='enrol-confirm-yes']").ClickAsync(new());
        confirmed.Should().BeTrue();

        await dialog.Find("[data-testid='enrol-confirm-no']").ClickAsync(new());
        cancelled.Should().BeTrue();
    }

    /// <summary>
    /// Walks up from the test binaries to the repository root and returns the component's source
    /// directory. Keeps the guard working regardless of the build output layout.
    /// </summary>
    private static string LocateComponentDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the test must be able to find the repository root to read component source");

        return Path.Combine(dir!.FullName, "src", "Apps", "Sorcha.Wallet.Pwa", "Components");
    }
}
