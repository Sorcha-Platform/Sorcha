// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// Tier-aware authorization (spec 139): tier-primary, role-secondary, with cross-tier
/// participation tools, service-tier rejection, and a non-empty consumer surface.
/// </summary>
public class McpAuthorizationServiceTests
{
    private readonly Mock<ICallerContext> _callerMock;
    private readonly McpAuthorizationService _service;

    public McpAuthorizationServiceTests()
    {
        _callerMock = new Mock<ICallerContext>();
        _service = new McpAuthorizationService(_callerMock.Object, Mock.Of<ILogger<McpAuthorizationService>>());
    }

    private void SetupCaller(Tier? tier, params string[] roles)
    {
        _callerMock.Setup(x => x.IsAuthenticated).Returns(true);
        _callerMock.Setup(x => x.Tier).Returns(tier);
        _callerMock.Setup(x => x.Roles).Returns(roles);
        _callerMock.Setup(x => x.Subject).Returns("test-user");
    }

    #region Admin tools — platform tier + admin role

    [Theory]
    [InlineData("sorcha_health_check")]
    [InlineData("sorcha_tenant_create")]
    [InlineData("sorcha_register_stats")]
    [InlineData("sorcha_token_revoke")]
    public void CanInvokeTool_AdminTool_PlatformAdmin_ReturnsTrue(string toolName)
    {
        SetupCaller(Tier.Platform, "sorcha:admin");
        _service.CanInvokeTool(toolName).Should().BeTrue();
    }

    [Theory]
    [InlineData("sorcha_health_check")]
    [InlineData("sorcha_tenant_create")]
    public void CanInvokeTool_AdminTool_PlatformDesigner_ReturnsFalse(string toolName)
    {
        SetupCaller(Tier.Platform, "sorcha:designer");
        _service.CanInvokeTool(toolName).Should().BeFalse();
    }

    [Fact]
    public void CanInvokeTool_AdminTool_Consumer_ReturnsFalse()
    {
        SetupCaller(Tier.Consumer);
        _service.CanInvokeTool("sorcha_register_stats").Should().BeFalse();
    }

    #endregion

    #region Designer tools — platform tier + designer role

    [Theory]
    [InlineData("sorcha_blueprint_create")]
    [InlineData("sorcha_schema_validate")]
    [InlineData("sorcha_workflow_instances")]
    public void CanInvokeTool_DesignerTool_PlatformDesigner_ReturnsTrue(string toolName)
    {
        SetupCaller(Tier.Platform, "sorcha:designer");
        _service.CanInvokeTool(toolName).Should().BeTrue();
    }

    [Fact]
    public void CanInvokeTool_DesignerTool_PlatformAdminWithoutDesigner_ReturnsFalse()
    {
        SetupCaller(Tier.Platform, "sorcha:admin");
        _service.CanInvokeTool("sorcha_blueprint_create").Should().BeFalse();
    }

    [Fact]
    public void CanInvokeTool_DesignerTool_Consumer_ReturnsFalse()
    {
        SetupCaller(Tier.Consumer);
        _service.CanInvokeTool("sorcha_blueprint_create").Should().BeFalse();
    }

    #endregion

    #region Participation tools — cross-tier (consumer OR platform), no role

    [Theory]
    [InlineData("sorcha_inbox_list")]
    [InlineData("sorcha_action_submit")]
    [InlineData("sorcha_workflow_status")]
    [InlineData("sorcha_transaction_history")]
    [InlineData("sorcha_wallet_info")]
    public void CanInvokeTool_ParticipationTool_Consumer_ReturnsTrue(string toolName)
    {
        SetupCaller(Tier.Consumer);
        _service.CanInvokeTool(toolName).Should().BeTrue();
    }

    [Theory]
    [InlineData("sorcha_inbox_list")]
    [InlineData("sorcha_action_submit")]
    public void CanInvokeTool_ParticipationTool_PlatformNoRole_ReturnsTrue(string toolName)
    {
        SetupCaller(Tier.Platform);
        _service.CanInvokeTool(toolName).Should().BeTrue();
    }

    #endregion

    #region Removed / service tier / edge cases

    [Fact]
    public void CanInvokeTool_WalletSign_Removed_ReturnsFalseForAllTiers()
    {
        SetupCaller(Tier.Consumer);
        _service.CanInvokeTool("sorcha_wallet_sign").Should().BeFalse();

        SetupCaller(Tier.Platform, "sorcha:admin");
        _service.CanInvokeTool("sorcha_wallet_sign").Should().BeFalse();
    }

    [Fact]
    public void CanInvokeTool_ServiceTier_ReturnsFalse()
    {
        SetupCaller(Tier.Service, "sorcha:admin");
        _service.CanInvokeTool("sorcha_register_stats").Should().BeFalse();
        _service.CanInvokeTool("sorcha_inbox_list").Should().BeFalse();
    }

    [Fact]
    public void CanInvokeTool_NotAuthenticated_ReturnsFalse()
    {
        _callerMock.Setup(x => x.IsAuthenticated).Returns(false);
        _service.CanInvokeTool("sorcha_inbox_list").Should().BeFalse();
    }

    [Fact]
    public void CanInvokeTool_UnrecognisedTier_ReturnsFalse()
    {
        SetupCaller(tier: null);
        _service.CanInvokeTool("sorcha_inbox_list").Should().BeFalse();
    }

    [Fact]
    public void CanInvokeTool_UnknownTool_ReturnsFalse()
    {
        SetupCaller(Tier.Platform, "sorcha:admin");
        _service.CanInvokeTool("sorcha_unknown_tool").Should().BeFalse();
    }

    #endregion

    #region GetAuthorizedTools

    [Fact]
    public void GetAuthorizedTools_PlatformAdmin_ReturnsAdminAndParticipationNotDesigner()
    {
        SetupCaller(Tier.Platform, "sorcha:admin");
        var tools = _service.GetAuthorizedTools();

        tools.Should().Contain("sorcha_health_check");        // admin
        tools.Should().Contain("sorcha_inbox_list");          // participation (cross-tier)
        tools.Should().NotContain("sorcha_blueprint_create"); // designer only
        tools.Should().NotContain("sorcha_wallet_sign");      // removed
    }

    [Fact]
    public void GetAuthorizedTools_Consumer_IsNonEmptyAndExcludesAdminDesigner()
    {
        SetupCaller(Tier.Consumer);
        var tools = _service.GetAuthorizedTools();

        tools.Should().NotBeEmpty(); // F136 shut-out fixed
        tools.Should().Contain("sorcha_inbox_list");
        tools.Should().Contain("sorcha_wallet_info");
        tools.Should().NotContain("sorcha_health_check");     // admin
        tools.Should().NotContain("sorcha_blueprint_create"); // designer
    }

    [Fact]
    public void GetAuthorizedTools_ServiceTier_ReturnsEmpty()
    {
        SetupCaller(Tier.Service, "sorcha:admin");
        _service.GetAuthorizedTools().Should().BeEmpty();
    }

    [Fact]
    public void GetAuthorizedTools_NotAuthenticated_ReturnsEmpty()
    {
        _callerMock.Setup(x => x.IsAuthenticated).Returns(false);
        _service.GetAuthorizedTools().Should().BeEmpty();
    }

    #endregion
}
