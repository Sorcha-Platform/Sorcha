// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Wallet.Service.Hubs;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="WalletHubGroups"/>. Phase 7 / US5 of Feature 118.
/// </summary>
public class WalletHubGroupsTests
{
    [Fact]
    public void CitizenWallet_DelegatesToExistingHelper()
    {
        var pid = Guid.Parse("11111111-2222-3333-4444-555555555555");

        WalletHubGroups.CitizenWallet(pid).Should().Be(WalletHub.GroupNameFor(pid));
        WalletHubGroups.CitizenWallet(pid).Should().Be("wallet:platform-user:11111111222233334444555555555555");
    }

    [Fact]
    public void Wallet_PreservesAddress()
    {
        WalletHubGroups.Wallet("sor1qexample").Should().Be("wallet:sor1qexample");
    }
}
