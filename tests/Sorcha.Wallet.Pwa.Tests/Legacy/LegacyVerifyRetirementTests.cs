// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using FluentAssertions;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Legacy;

/// <summary>
/// Retirement assertion tests (Feature 164, B3 US4): confirms the legacy paste-based VerifyFlow
/// component has been removed from the shared component library, and the PWA Verify page no
/// longer references it.
/// </summary>
public sealed class LegacyVerifyRetirementTests
{
    private static string SharedComponentsVerifyPath => Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.UI", "Sorcha.UI.Components.User", "Components", "Verify");

    private static string PwaVerifyRazorPath => Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Wallet.Pwa", "Pages", "Verify.razor");

    [Fact]
    public void VerifyFlow_IsDeletedFromSharedLibrary()
    {
        var filePath = Path.Combine(SharedComponentsVerifyPath, "VerifyFlow.razor");
        File.Exists(filePath).Should().BeFalse(
            because: "VerifyFlow.razor must be deleted from the shared library in US4 — it is the paste-based legacy component");
    }

    [Fact]
    public void PwaVerifyRazor_DoesNotReferenceVerifyFlow()
    {
        if (!File.Exists(PwaVerifyRazorPath)) return;

        var content = File.ReadAllText(PwaVerifyRazorPath);
        content.Should().NotContain("VerifyFlow",
            because: "the PWA Verify.razor must not reference the legacy VerifyFlow after the B3 US2 rewire");
    }
}
