// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.CitizenWallet.Abstractions.Constants;

namespace Sorcha.CitizenWallet.Abstractions.Tests;

public sealed class ConstantsTests
{
    [Fact]
    public void DerivationContexts_CitizenHolder_IsExpectedString()
    {
        DerivationContexts.CitizenHolder.Should().Be("sorcha:citizen-holder");
    }

    [Fact]
    public void DerivationContexts_CitizenStatusSigning_IsExpectedString()
    {
        DerivationContexts.CitizenStatusSigning.Should().Be("sorcha:citizen-status-signing");
    }

    [Fact]
    public void DelegatedCapabilities_PresentationHolderKeyBinding_IsExpectedString()
    {
        DelegatedCapabilities.PresentationHolderKeyBinding
            .Should().Be("presentation.holder-key-binding");
    }

    [Fact]
    public void VctUris_CitizenDeviceDelegationV1_IsExpectedUri()
    {
        VctUris.CitizenDeviceDelegationV1
            .Should().Be("https://sorcha.dev/vc/citizen-device-delegation/v1");
    }

    [Theory]
    [InlineData("AssuredIdentityV1", "https://sorcha.dev/vc/assured-identity/v1")]
    [InlineData("DrivingLicenceV1", "https://sorcha.dev/vc/driving-licence/v1")]
    [InlineData("BlueBadgeV1", "https://sorcha.dev/vc/blue-badge/v1")]
    [InlineData("MembershipV1", "https://sorcha.dev/vc/membership/v1")]
    [InlineData("LicenceV1", "https://sorcha.dev/vc/licence/v1")]
    [InlineData("CouncilDigitalIdV1", "https://sorcha.dev/vc/council-digital-id/v1")]
    [InlineData("VerifiedInvoiceV1", "https://sorcha.dev/vc/verified-invoice/v1")]
    [InlineData("TradeFinanceV1", "https://sorcha.dev/vc/trade-finance/v1")]
    [InlineData("PlanningPermissionV1", "https://sorcha.dev/vc/planning-permission/v1")]
    [InlineData("BuildingWarrantV1", "https://sorcha.dev/vc/building-warrant/v1")]
    [InlineData("CompletionCertificateV1", "https://sorcha.dev/vc/completion-certificate/v1")]
    [InlineData("JobAssignmentV1", "https://sorcha.dev/vc/job-assignment/v1")]
    [InlineData("ServiceCompletionV1", "https://sorcha.dev/vc/service-completion/v1")]
    [InlineData("ForestProductDppV1", "https://sorcha.dev/vc/forest-product-dpp/v1")]
    [InlineData("CyberEssentialsUacV1", "https://sorcha.dev/vc/cyber-essentials-uac/v1")]
    [InlineData("RefurbishmentCertificateV1", "https://sorcha.dev/vc/refurbishment-certificate/v1")]
    [InlineData("BuildingPermitV1", "https://sorcha.dev/vc/building-permit/v1")]
    public void VctUris_CanonicalConstants_HaveExpectedLowercaseUri(string field, string expected)
    {
        var value = (string)typeof(VctUris).GetField(field)!.GetValue(null)!;
        value.Should().Be(expected);
        value.Should().Be(value.ToLowerInvariant(), "VCT URIs are lowercase kebab-case");
    }
}
