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

    [Fact]
    public void JwtAudiences_CitizenWallet_IsExpectedString()
    {
        JwtAudiences.CitizenWallet.Should().Be("sorcha:citizen-wallet");
    }
}
