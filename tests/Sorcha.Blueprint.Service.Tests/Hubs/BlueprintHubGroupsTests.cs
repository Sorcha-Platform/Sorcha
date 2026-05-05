// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Hubs;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="BlueprintHubGroups"/>. Phase 7 / US5 of Feature 118.
/// </summary>
public class BlueprintHubGroupsTests
{
    [Fact]
    public void Wallet_PreservesBech32Address()
    {
        var addr = "sor1q9ml6...placeholder...";
        BlueprintHubGroups.Wallet(addr).Should().Be("wallet:" + addr);
    }

    [Fact]
    public void Instance_UsesNoHyphenGuidFormat()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        BlueprintHubGroups.Instance(id).Should().Be("instance:11111111222233334444555555555555");
    }

    [Fact]
    public void Org_UsesNoHyphenGuidFormat()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        BlueprintHubGroups.Org(id).Should().Be("org:aaaaaaaabbbbccccddddeeeeeeeeeeee");
    }

    [Fact]
    public void LegacyEventsHubUser_PreservesRawString()
    {
        BlueprintHubGroups.LegacyEventsHubUser("user-claim-string").Should().Be("user:user-claim-string");
    }

    [Fact]
    public void LegacyEventsHubOrg_PreservesRawString()
    {
        BlueprintHubGroups.LegacyEventsHubOrg("org-claim-string").Should().Be("org:org-claim-string");
    }
}
