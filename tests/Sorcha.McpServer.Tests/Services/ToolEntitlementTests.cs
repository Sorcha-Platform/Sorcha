// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.McpServer.Services;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>Direct coverage of the tier→tool entitlement table (spec 139, contracts §2).</summary>
public class ToolEntitlementTests
{
    [Fact]
    public void All_DoesNotContainRemovedWalletSign()
    {
        ToolEntitlements.All.Select(e => e.ToolName).Should().NotContain("sorcha_wallet_sign");
    }

    [Fact]
    public void IsPermitted_AdminTool_RequiresPlatformAndAdminRole()
    {
        ToolEntitlements.IsPermitted("sorcha_register_stats", Tier.Platform, ["sorcha:admin"]).Should().BeTrue();
        ToolEntitlements.IsPermitted("sorcha_register_stats", Tier.Platform, ["sorcha:designer"]).Should().BeFalse();
        ToolEntitlements.IsPermitted("sorcha_register_stats", Tier.Consumer, ["sorcha:admin"]).Should().BeFalse();
    }

    [Fact]
    public void IsPermitted_ParticipationTool_IsCrossTierWithoutRole()
    {
        ToolEntitlements.IsPermitted("sorcha_inbox_list", Tier.Consumer, []).Should().BeTrue();
        ToolEntitlements.IsPermitted("sorcha_inbox_list", Tier.Platform, []).Should().BeTrue();
    }

    [Fact]
    public void IsPermitted_FailsClosed_OnNullTierUnknownToolAndServiceTier()
    {
        ToolEntitlements.IsPermitted("sorcha_inbox_list", null, []).Should().BeFalse();
        ToolEntitlements.IsPermitted("sorcha_unknown", Tier.Platform, ["sorcha:admin"]).Should().BeFalse();
        ToolEntitlements.IsPermitted("sorcha_inbox_list", Tier.Service, []).Should().BeFalse();
    }

    [Fact]
    public void VisibleTools_Consumer_IsNonEmptyAndExcludesPrivilegedSlices()
    {
        var tools = ToolEntitlements.VisibleTools(Tier.Consumer, []);

        tools.Should().NotBeEmpty();
        tools.Should().Contain("sorcha_inbox_list");
        tools.Should().Contain("sorcha_wallet_info");
        tools.Should().NotContain("sorcha_health_check");
        tools.Should().NotContain("sorcha_blueprint_create");
    }

    [Fact]
    public void VisibleTools_PlatformDesigner_HasDesignerAndParticipationNotAdmin()
    {
        var tools = ToolEntitlements.VisibleTools(Tier.Platform, ["sorcha:designer"]);

        tools.Should().Contain("sorcha_blueprint_create");
        tools.Should().Contain("sorcha_inbox_list");
        tools.Should().NotContain("sorcha_health_check");
    }
}
