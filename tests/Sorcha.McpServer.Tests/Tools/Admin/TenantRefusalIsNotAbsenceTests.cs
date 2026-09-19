// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Shared;
using Sorcha.ServiceClients.Tenant;

using Xunit;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// #1673 — a Tenant 403 must not be reported as a missing organisation.
/// </summary>
/// <remarks>
/// <para>
/// Cold-start run #6: the Provider agent looked at the counterparty's organisation and was
/// correctly refused — it is not an administrator there. The org boundary worked. But
/// <c>sorcha_org_user_audit</c> answered <c>NotFound</c> and <c>sorcha_user_list</c> a bare error,
/// because <c>TenantServiceClient.GetRawAsync</c> collapsed every non-success into null.
/// </para>
/// <para>
/// "That organisation does not exist" is a conclusion an agent acts on, and in a two-party
/// exchange it is the wrong one. Fourth instance of this shape after #1659, #1641 and #1670.
/// </para>
/// </remarks>
public sealed class TenantRefusalIsNotAbsenceTests
{
    private const string OtherOrg = "072a9b81-8e0e-4300-b7db-c3c8d9567b90";

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<ITenantServiceClient> _tenant = new();

    private void Allow(string tool)
    {
        _auth.Setup(x => x.CanInvokeTool(tool)).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable(It.IsAny<string>())).Returns(true);
    }

    [Fact]
    public async Task OrgUserAudit_WhenForbidden_SaysRefusedAndThatTheOrganisationExists()
    {
        Allow("sorcha_org_user_audit");
        _tenant.Setup(t => t.GetOrganizationUsersAsync(OtherOrg, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await AuditTool().InvokeAsync(OtherOrg);

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("not permitted").And.Contain("It exists");
        result.Message.Should().NotContain("was not found");
    }

    [Fact]
    public async Task OrgUserAudit_WhenGenuinelyMissing_StillSaysNotFound()
    {
        // The counterfactual: a real 404 must keep reporting absence.
        Allow("sorcha_org_user_audit");
        _tenant.Setup(t => t.GetOrganizationUsersAsync(OtherOrg, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.NotFound, null));

        var result = await AuditTool().InvokeAsync(OtherOrg);

        result.Status.Should().Be("NotFound");
        result.Message.Should().Contain("was not found");
    }

    [Fact]
    public async Task UserList_WhenForbidden_SaysRefusedRatherThanFailedToRetrieve()
    {
        Allow("sorcha_user_list");
        _tenant.Setup(t => t.ListUsersAsync(OtherOrg, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.Forbidden, null));

        var result = await ListTool().ListUsersAsync(OtherOrg);

        result.Status.Should().Be("Refused");
        result.Message.Should().Contain("not permitted").And.Contain("It exists");
        result.Message.Should().NotContain("Failed to retrieve");
    }

    [Fact]
    public async Task UserList_WhenTheServiceErrors_StillReportsAnError()
    {
        Allow("sorcha_user_list");
        _tenant.Setup(t => t.ListUsersAsync(OtherOrg, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceReadResult(HttpStatusCode.InternalServerError, null));

        var result = await ListTool().ListUsersAsync(OtherOrg);

        result.Status.Should().Be("Error");
        result.Message.Should().NotContain("not permitted");
    }

    private OrgUserAuditTool AuditTool() => new(
        _auth.Object, _availability.Object, _tenant.Object, Mock.Of<ILogger<OrgUserAuditTool>>());

    private UserListTool ListTool() => new(
        _auth.Object, _availability.Object, _tenant.Object, Mock.Of<ILogger<UserListTool>>());
}
