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

    /// <summary>
    /// The 8 Feature 140 Wave-3 citizen self-service tools are CONSUMER tier only: a consumer-tier
    /// caller may invoke them, a platform-admin context may NOT (they are the consumer-facing slice).
    /// </summary>
    [Theory]
    [InlineData("sorcha_my_credentials")]
    [InlineData("sorcha_my_devices")]
    [InlineData("sorcha_my_device_rename")]
    [InlineData("sorcha_my_device_revoke")]
    [InlineData("sorcha_my_persona")]
    [InlineData("sorcha_pending_applications")]
    [InlineData("sorcha_my_presentations")]
    [InlineData("sorcha_my_invitations")]
    public void Wave3Tools_AreConsumerOnly_NoRole(string toolName)
    {
        // Consumer tier (no role) is allowed — citizen self-service.
        ToolEntitlements.IsPermitted(toolName, Tier.Consumer, []).Should().BeTrue();
        // A platform admin does NOT see these consumer tools.
        ToolEntitlements.IsPermitted(toolName, Tier.Platform, ["sorcha:admin"]).Should().BeFalse();
        ToolEntitlements.IsPermitted(toolName, Tier.Platform, []).Should().BeFalse();
        // Fail-closed on no tier / service tier.
        ToolEntitlements.IsPermitted(toolName, null, []).Should().BeFalse();
        ToolEntitlements.IsPermitted(toolName, Tier.Service, []).Should().BeFalse();
    }

    /// <summary>The consumer surface (F139 guarantee) now also includes the Wave-3 self-service slice.</summary>
    [Fact]
    public void VisibleTools_Consumer_IncludesWave3SelfServiceTools()
    {
        var tools = ToolEntitlements.VisibleTools(Tier.Consumer, []);

        tools.Should().Contain("sorcha_my_credentials");
        tools.Should().Contain("sorcha_my_devices");
        tools.Should().Contain("sorcha_my_device_rename");
        tools.Should().Contain("sorcha_my_device_revoke");
        tools.Should().Contain("sorcha_my_persona");
        tools.Should().Contain("sorcha_pending_applications");
        tools.Should().Contain("sorcha_my_presentations");
        tools.Should().Contain("sorcha_my_invitations");
    }

    /// <summary>The Wave-3 consumer tools must NOT leak into a platform admin's visible set.</summary>
    [Fact]
    public void VisibleTools_PlatformAdmin_ExcludesWave3ConsumerTools()
    {
        var tools = ToolEntitlements.VisibleTools(Tier.Platform, ["sorcha:admin"]);

        tools.Should().NotContain("sorcha_my_credentials");
        tools.Should().NotContain("sorcha_my_persona");
        tools.Should().NotContain("sorcha_my_invitations");
    }

    /// <summary>The 8 Feature 140 Wave-1 register-control/federation tools are platform + admin only.</summary>
    [Theory]
    [InlineData("sorcha_register_subscribe")]
    [InlineData("sorcha_register_unsubscribe")]
    [InlineData("sorcha_register_sync_state")]
    [InlineData("sorcha_register_relationship")]
    [InlineData("sorcha_transaction_status")]
    [InlineData("sorcha_transaction_inclusion_proof")]
    [InlineData("sorcha_transaction_verification_bundle")]
    [InlineData("sorcha_transaction_revoke")]
    public void Wave1Tools_RequirePlatformAndAdminRole(string toolName)
    {
        ToolEntitlements.IsPermitted(toolName, Tier.Platform, ["sorcha:admin"]).Should().BeTrue();
        ToolEntitlements.IsPermitted(toolName, Tier.Platform, ["sorcha:designer"]).Should().BeFalse();
        ToolEntitlements.IsPermitted(toolName, Tier.Consumer, ["sorcha:admin"]).Should().BeFalse();
        ToolEntitlements.IsPermitted(toolName, Tier.Platform, []).Should().BeFalse();
    }
}
