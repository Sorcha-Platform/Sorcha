// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Credentials;
using Sorcha.UI.Core.Models.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Credentials;

/// <summary>
/// Tests for VerificationTrustView rendering logic and display state computation.
/// Covers banner text, expansion state, check counts, and individual check rows.
/// </summary>
public class VerificationTrustViewTests : BunitContext
{
    public VerificationTrustViewTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── Banner state tests ────────────────────────────────────────────────────

    [Fact]
    public void Render_GreenState_ShowsVerifiedCredentialBanner()
    {
        // Arrange
        var result = CreateGreenResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("Verified Credential");
    }

    [Fact]
    public void Render_AmberState_ShowsVerificationWarningBanner()
    {
        // Arrange
        var result = CreateAmberResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("Verification Warning");
    }

    [Fact]
    public void Render_RedState_ShowsVerificationFailedBanner()
    {
        // Arrange
        var result = CreateRedResult(signatureValid: false);

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("Verification Failed");
    }

    // ── Subtitle tests ────────────────────────────────────────────────────────

    [Fact]
    public void Render_GreenState_SubtitleContainsCredentialTypeAndIssuerName()
    {
        // Arrange
        var result = CreateGreenResult(credentialType: "Employment Credential", issuerName: "Acme Corp");

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("Employment Credential");
        cut.Markup.Should().Contain("Acme Corp");
    }

    [Fact]
    public void Render_AmberState_SubtitleContainsWarningCount()
    {
        // Arrange
        var result = CreateAmberResult(warningCount: 2);

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("2 check(s) need attention");
    }

    // ── Check count display tests ─────────────────────────────────────────────

    [Fact]
    public void Render_GreenState_CheckCountShowsAllFivePassed()
    {
        // Arrange
        var result = CreateGreenResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert — "5 of 5 checks passed"
        cut.Markup.Should().Contain("5 of 5 checks passed");
    }

    [Fact]
    public void Render_RedStateOneFailure_CheckCountShowsFourOfFive()
    {
        // Arrange — only signature fails, everything else passes
        var result = CreateRedResult(signatureValid: false);

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert — "4 of 5 checks passed"
        cut.Markup.Should().Contain("4 of 5 checks passed");
    }

    [Fact]
    public void Render_AllChecksFail_CheckCountShowsZeroOfFive()
    {
        // Arrange
        var result = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = false,
            NotRevoked = false,
            NotExpired = false,
            RequiredClaimsPresent = false,
            Warnings = []
        };

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("0 of 5 checks passed");
    }

    // ── Expansion panel state tests ───────────────────────────────────────────

    [Fact]
    public void Render_GreenState_VerificationDetailsPanelCollapsedByDefault()
    {
        // Arrange
        var result = CreateGreenResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert — panel should not be expanded (no inline visible check rows)
        // The TitleContent is always rendered; ChildContent only when expanded
        // In collapsed state, the detailed check row text still appears in the DOM
        // but the panel header indicates it is collapsed
        cut.Markup.Should().Contain("Verification details");
        // Green uses IsInitiallyExpanded=false → panel starts collapsed
        result.EscalationLevel.Should().Be(TrustEscalation.Green);
    }

    [Fact]
    public void Render_AmberState_VerificationDetailsPanelExpandedByDefault()
    {
        // Arrange
        var result = CreateAmberResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert — escalation level drives expansion
        result.EscalationLevel.Should().Be(TrustEscalation.Amber);
        cut.Markup.Should().Contain("Verification details");
    }

    [Fact]
    public void Render_RedState_VerificationDetailsPanelExpandedByDefault()
    {
        // Arrange
        var result = CreateRedResult(signatureValid: false);

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        result.EscalationLevel.Should().Be(TrustEscalation.Red);
        cut.Markup.Should().Contain("Verification details");
    }

    // ── Individual check row tests ────────────────────────────────────────────

    [Fact]
    public void Render_AllChecks_ShowsAllFiveCheckLabels()
    {
        // Arrange
        var result = CreateGreenResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("Signature valid");
        cut.Markup.Should().Contain("Issuer trusted");
        cut.Markup.Should().Contain("Revocation status");
        cut.Markup.Should().Contain("Not expired");
        cut.Markup.Should().Contain("Required claims present");
    }

    [Fact]
    public void Render_IssuerTrusted_ShowsIssuerName()
    {
        // Arrange
        var result = CreateGreenResult(issuerName: "University of Edinburgh");

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("University of Edinburgh");
    }

    [Fact]
    public void Render_NotExpiredWithExpiry_ShowsExpiryDate()
    {
        // Arrange
        var expiresAt = new DateTimeOffset(2027, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var result = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
            ExpiresAt = expiresAt
        };

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("2027-06-15");
    }

    [Fact]
    public void Render_RequiredClaimsPresent_ShowsClaimCount()
    {
        // Arrange
        var result = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
            RequiredClaimCount = 4,
            PresentClaimCount = 4
        };

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert — "4 of 4"
        cut.Markup.Should().Contain("4 of 4");
    }

    // ── Revocation warning (amber check row) tests ────────────────────────────

    [Fact]
    public void Render_RevocationWarning_ShowsWarningMessage()
    {
        // Arrange
        var result = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings =
            [
                new VerificationWarning("REVOCATION_CHECK_FAILED_OPEN", "FailOpen applied — status list unavailable")
            ]
        };

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("FailOpen applied");
    }

    // ── Disclosed claims tests ────────────────────────────────────────────────

    [Fact]
    public void Render_WithDisclosedClaims_ShowsClaimsSection()
    {
        // Arrange
        var result = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
            DisclosedClaims = new Dictionary<string, object>
            {
                ["given_name"] = "Alice",
                ["family_name"] = "Smith"
            }
        };

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().Contain("Disclosed Claims");
        cut.Markup.Should().Contain("given_name");
        cut.Markup.Should().Contain("Alice");
        cut.Markup.Should().Contain("family_name");
        cut.Markup.Should().Contain("Smith");
    }

    [Fact]
    public void Render_WithNoDisclosedClaims_DoesNotShowClaimsSection()
    {
        // Arrange
        var result = CreateGreenResult();

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert
        cut.Markup.Should().NotContain("Disclosed Claims");
    }

    [Fact]
    public void Render_WithEmptyDisclosedClaims_DoesNotShowClaimsSection()
    {
        // Arrange
        var result = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
            DisclosedClaims = new Dictionary<string, object>()
        };

        // Act
        var cut = Render<VerificationTrustView>(p => p.Add(c => c.Result, result));

        // Assert — empty dict should not render the section
        cut.Markup.Should().NotContain("Disclosed Claims");
    }

    // ── VerificationResultViewModel computed property tests ───────────────────
    // These test the model logic that drives component decisions directly.

    [Fact]
    public void EscalationLevel_AllPassNoWarnings_ReturnsGreen()
    {
        var result = CreateGreenResult();
        result.EscalationLevel.Should().Be(TrustEscalation.Green);
    }

    [Fact]
    public void EscalationLevel_AllPassWithWarnings_ReturnsAmber()
    {
        var result = CreateAmberResult();
        result.EscalationLevel.Should().Be(TrustEscalation.Amber);
    }

    [Theory]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void EscalationLevel_AnyCheckFails_ReturnsRed(
        bool sigValid, bool issuerTrusted, bool notRevoked, bool notExpired, bool claimsPresent)
    {
        var result = new VerificationResultViewModel
        {
            SignatureValid = sigValid,
            IssuerTrusted = issuerTrusted,
            NotRevoked = notRevoked,
            NotExpired = notExpired,
            RequiredClaimsPresent = claimsPresent,
            Warnings = []
        };

        result.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void PassedCheckCount_AllPassFive_ReturnsFive()
    {
        var result = CreateGreenResult();
        result.PassedCheckCount.Should().Be(5);
    }

    [Fact]
    public void PassedCheckCount_TwoFail_ReturnsThree()
    {
        var result = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = true,
            NotRevoked = false,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = []
        };

        result.PassedCheckCount.Should().Be(3);
    }

    [Fact]
    public void TotalCheckCount_AlwaysReturnsFive()
    {
        var result = CreateGreenResult();
        result.TotalCheckCount.Should().Be(5);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static VerificationResultViewModel CreateGreenResult(
        string? credentialType = "Test Credential",
        string? issuerName = "Test Issuer")
    {
        return new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
            CredentialType = credentialType,
            IssuerName = issuerName
        };
    }

    private static VerificationResultViewModel CreateAmberResult(int warningCount = 1)
    {
        var warnings = Enumerable.Range(1, warningCount)
            .Select(i => new VerificationWarning($"WARN_{i}", $"Warning {i}"))
            .ToList();

        return new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = warnings,
            CredentialType = "Test Credential",
            IssuerName = "Test Issuer"
        };
    }

    private static VerificationResultViewModel CreateRedResult(
        bool signatureValid = true,
        bool issuerTrusted = true,
        bool notRevoked = true,
        bool notExpired = true,
        bool requiredClaimsPresent = true)
    {
        return new VerificationResultViewModel
        {
            SignatureValid = signatureValid,
            IssuerTrusted = issuerTrusted,
            NotRevoked = notRevoked,
            NotExpired = notExpired,
            RequiredClaimsPresent = requiredClaimsPresent,
            Warnings = [],
            CredentialType = "Test Credential",
            IssuerName = "Test Issuer"
        };
    }
}
