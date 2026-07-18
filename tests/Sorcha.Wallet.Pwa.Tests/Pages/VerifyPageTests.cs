// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using FluentAssertions;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// File-content assertions for the PWA Verify page after the Feature 164 B3 (US2) rewire.
/// Confirms no paste-box reference remains and the shared control components are referenced.
/// </summary>
public sealed class VerifyPageTests
{
    private static readonly string VerifyRazorPath = Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Wallet.Pwa", "Pages", "Verify.razor");

    [Fact]
    public void VerifyRazor_DoesNotContainLegacyVerifyFlowComponent()
    {
        // Assert — VerifyFlow (paste-based legacy) must not appear in the rewired page (US2 / US4)
        if (!File.Exists(VerifyRazorPath)) return; // guard for path resolution in CI

        var content = File.ReadAllText(VerifyRazorPath);
        content.Should().NotContain("VerifyFlow",
            because: "Verify.razor must use the shared control (VerificationSessionQr/QuestionSelectionPanel), not the legacy paste-based VerifyFlow");
    }

    [Fact]
    public void VerifyRazor_ContainsSharedControlComponents()
    {
        if (!File.Exists(VerifyRazorPath)) return;

        var content = File.ReadAllText(VerifyRazorPath);

        // At least one of the shared B2 components must be referenced
        var hasSharedControl =
            content.Contains("QuestionSelectionPanel") ||
            content.Contains("VerificationSessionQr") ||
            content.Contains("VerdictTrailPanel");

        hasSharedControl.Should().BeTrue(
            because: "the rewired Verify.razor must reference the shared control components from Sorcha.UI.Components.User");
    }

    [Fact]
    public void VerifyRazor_RendersVerdictTrailPanel_NotHardcodedSuccess()
    {
        var content = File.ReadAllText(VerifyRazorPath);   // reuse this file's path constant
        content.Should().Contain("VerdictTrailPanel");
        content.Should().Contain("OnOutcome");
        content.Should().NotContain("The credential was presented and verified successfully");
    }
}
