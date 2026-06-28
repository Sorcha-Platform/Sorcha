// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using FluentAssertions;
using Xunit;

namespace Sorcha.Verifier.Tests.Legacy;

/// <summary>
/// Retirement assertion tests (Feature 164, B3 US4): confirms the legacy desk verify machinery
/// has been removed. Tests fail before deletion and pass after.
/// </summary>
public sealed class LegacyVerifyRetirementTests
{
    private static string DeskServicesPath => Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Verifier", "Services");

    private static string DeskEndpointsPath => Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Verifier", "Endpoints");

    private static string DeskPagesPath => Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Verifier", "Components", "Pages");

    [Fact]
    public void PresentationRequestBuilder_IsDeleted()
    {
        var filePath = Path.Combine(DeskServicesPath, "PresentationRequestBuilder.cs");
        File.Exists(filePath).Should().BeFalse(
            because: "PresentationRequestBuilder.cs must be deleted in US4 — the bespoke desk flow is retired");
    }

    [Fact]
    public void InMemoryVerifierSessionStore_IsDeleted()
    {
        var filePath = Path.Combine(DeskServicesPath, "InMemoryVerifierSessionStore.cs");
        File.Exists(filePath).Should().BeFalse(
            because: "InMemoryVerifierSessionStore.cs must be deleted in US4 — session store is replaced by HAIP service");
    }

    [Fact]
    public void PresentationResponseEndpoints_IsDeleted()
    {
        var filePath = Path.Combine(DeskEndpointsPath, "PresentationResponseEndpoints.cs");
        File.Exists(filePath).Should().BeFalse(
            because: "PresentationResponseEndpoints.cs must be deleted in US4 — /r/{id}/response and /status are retired");
    }

    [Fact]
    public void OutcomeRazor_IsDeleted()
    {
        var filePath = Path.Combine(DeskPagesPath, "Outcome.razor");
        File.Exists(filePath).Should().BeFalse(
            because: "Outcome.razor must be deleted in US4 — the desk verdict page is replaced by the shared VerdictTrailPanel");
    }
}
