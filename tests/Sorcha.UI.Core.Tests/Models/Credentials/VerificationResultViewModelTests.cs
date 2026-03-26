// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Models.Credentials;

/// <summary>
/// Tests for VerificationResultViewModel computed properties.
/// </summary>
public class VerificationResultViewModelTests
{
    private static VerificationResultViewModel AllPassNoWarnings() => new()
    {
        SignatureValid = true,
        IssuerTrusted = true,
        NotRevoked = true,
        NotExpired = true,
        RequiredClaimsPresent = true,
        Warnings = [],
        IssuerName = "Test Issuer",
        CredentialType = "TestCredential",
        ExpiresAt = null,
        DisclosedClaims = new Dictionary<string, object>(),
    };

    // EscalationLevel tests

    [Fact]
    public void EscalationLevel_AllChecksPassNoWarnings_IsGreen()
    {
        var vm = AllPassNoWarnings();

        vm.EscalationLevel.Should().Be(TrustEscalation.Green);
    }

    [Fact]
    public void EscalationLevel_AllChecksPassWithWarnings_IsAmber()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [new VerificationWarning("WARN001", "Minor issue")],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Amber);
    }

    [Fact]
    public void EscalationLevel_SignatureInvalid_IsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_IssuerNotTrusted_IsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = false,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_Revoked_IsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = false,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_Expired_IsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = false,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_RequiredClaimsMissing_IsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = false,
            Warnings = [],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_MultipleFailures_IsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = false,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_FailureWithWarnings_IsRed()
    {
        // Failure takes precedence over warnings → still Red
        var vm = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [new VerificationWarning("WARN001", "Minor issue")],
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    // PassedCheckCount / TotalCheckCount tests

    [Fact]
    public void TotalCheckCount_IsAlways5()
    {
        AllPassNoWarnings().TotalCheckCount.Should().Be(5);
    }

    [Fact]
    public void PassedCheckCount_AllPass_Is5()
    {
        AllPassNoWarnings().PassedCheckCount.Should().Be(5);
    }

    [Fact]
    public void PassedCheckCount_OneFailure_Is4()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.PassedCheckCount.Should().Be(4);
    }

    [Fact]
    public void PassedCheckCount_TwoFailures_Is3()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = false,
            RequiredClaimsPresent = true,
            Warnings = [],
        };

        vm.PassedCheckCount.Should().Be(3);
    }

    [Fact]
    public void PassedCheckCount_AllFail_Is0()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = false,
            IssuerTrusted = false,
            NotRevoked = false,
            NotExpired = false,
            RequiredClaimsPresent = false,
            Warnings = [],
        };

        vm.PassedCheckCount.Should().Be(0);
    }
}
