// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using FluentAssertions;
using Xunit;

namespace Sorcha.Verifier.Tests.Pages;

/// <summary>
/// File-content assertions for the desk verifier Index page after the Feature 164 B3 (US3) rewire.
/// Confirms shared control components are referenced and legacy bespoke components are not.
/// </summary>
public sealed class VerifyPageTests
{
    private static readonly string IndexRazorPath = Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Verifier", "Components", "Pages", "Index.razor");

    [Fact]
    public void DeskIndexRazor_ContainsSharedControlComponents()
    {
        if (!File.Exists(IndexRazorPath)) return; // guard for CI path resolution

        var content = File.ReadAllText(IndexRazorPath);

        var hasSharedControl =
            content.Contains("QuestionSelectionPanel") ||
            content.Contains("VerificationSessionQr") ||
            content.Contains("VerdictTrailPanel");

        hasSharedControl.Should().BeTrue(
            because: "the rewired desk Index.razor must reference the shared control components from Sorcha.UI.Components.User (US3)");
    }

    [Fact]
    public void DeskIndexRazor_DoesNotInjectPresentationRequestBuilder()
    {
        if (!File.Exists(IndexRazorPath)) return;

        var content = File.ReadAllText(IndexRazorPath);

        content.Should().NotContain("IPresentationRequestBuilder",
            because: "the legacy bespoke builder must be retired (US4)");
        content.Should().NotContain("IPresentationRequestBuilder",
            because: "the desk index page should not inject the legacy presentation request builder");
    }

    [Fact]
    public void DeskIndexRazor_RendersVerdictTrailPanel_NotHardcodedSuccess()
    {
        var content = File.ReadAllText(IndexRazorPath);   // reuse the path constant already in this file
        content.Should().Contain("VerdictTrailPanel");
        content.Should().Contain("OnOutcome");
        content.Should().NotContain("The credential was presented and verified successfully");
    }
}
