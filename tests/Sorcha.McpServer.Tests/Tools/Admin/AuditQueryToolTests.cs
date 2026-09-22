// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// #1648: <c>sorcha_audit_query</c> reads the caller's own organisation audit log, where services now
/// record refusals with their reason. Before this it returned NotSupported, which is where the
/// 2026-09-15 cold-start run ended: an agent refused by the platform had nowhere to learn why.
/// </summary>
public class AuditQueryToolTests
{
    private const string OrgId = "00000000-0000-0000-0000-000000000001";

    private const string RefusalPage = """
        {
          "events": [
            {
              "id": 42,
              "timestamp": "2026-09-15T17:33:43Z",
              "eventType": "PermissionDenied",
              "identityId": "00000000-0000-0001-0000-000000000001",
              "success": false,
              "details": {
                "source": "service-refusal",
                "service": "wallet-service",
                "action": "wallet.sign",
                "resourceType": "wallet",
                "resourceId": "ws11qpws9aaz",
                "reason": "signature refused: the caller holds no active signing delegation on this wallet"
              }
            },
            {
              "id": 41,
              "timestamp": "2026-09-15T17:30:00Z",
              "eventType": "Login",
              "identityId": "00000000-0000-0001-0000-000000000001",
              "success": true,
              "details": null
            }
          ],
          "totalCount": 2,
          "page": 1,
          "pageSize": 50
        }
        """;

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<ICallerContext> _caller = new();
    private readonly Mock<ITenantServiceClient> _tenant = new();

    public AuditQueryToolTests()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_audit_query")).Returns(true);
        _availability.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
        _caller.SetupGet(c => c.OrganizationId).Returns(OrgId);
        _tenant.Setup(t => t.GetOrganizationAuditEventsAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpStatusCode.OK, RefusalPage));
    }

    private AuditQueryTool CreateTool() => new(
        _auth.Object, _availability.Object, _caller.Object, _tenant.Object,
        Mock.Of<ILogger<AuditQueryTool>>());

    [Fact]
    public async Task NotEntitled_ReturnsUnauthorizedWithoutReadingTheLog()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_audit_query")).Returns(false);

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("Unauthorized");
        VerifyNoRead();
    }

    [Fact]
    public async Task TokenWithoutAnOrganisation_ReturnsErrorWithoutReadingTheLog()
    {
        _caller.SetupGet(c => c.OrganizationId).Returns((string?)null);

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("Error");
        result.Message.Should().ContainEquivalentOf("organisation");
        VerifyNoRead();
    }

    /// <summary>
    /// The organisation comes from the caller's token only. An argument naming an organisation would
    /// let an agent ask for another organisation's log, and a SystemAdmin token would be exempt from
    /// the Tenant Service's own organisation check.
    /// </summary>
    [Fact]
    public void TheToolTakesNoOrganisationArgument()
    {
        var parameters = typeof(AuditQueryTool)
            .GetMethod(nameof(AuditQueryTool.QueryAuditLogsAsync))!
            .GetParameters()
            .Select(p => p.Name!.ToLowerInvariant());

        parameters.Should().NotContain(name => name.Contains("tenant") || name.Contains("org"));
    }

    [Fact]
    public async Task Success_MapsARefusalEntryWithItsReason()
    {
        var result = await CreateTool().QueryAuditLogsAsync(eventType: "PermissionDenied");

        result.Status.Should().Be("Success");
        result.OrganizationId.Should().Be(OrgId);
        result.TotalCount.Should().Be(2);
        result.Entries.Should().HaveCount(2);

        var refusal = result.Entries[0];
        refusal.AuditId.Should().Be("42");
        refusal.EventType.Should().Be("PermissionDenied");
        refusal.Success.Should().BeFalse();
        refusal.Action.Should().Be("wallet.sign");
        refusal.ResourceType.Should().Be("wallet");
        refusal.ResourceId.Should().Be("ws11qpws9aaz");
        refusal.Service.Should().Be("wallet-service");
        refusal.Reason.Should().Contain("no active signing delegation");
        refusal.PlatformUserId.Should().Be("00000000-0000-0001-0000-000000000001");

        // An entry that names no action falls back to its event type, so Action is never blank.
        result.Entries[1].Action.Should().Be("Login");
        result.Entries[1].Reason.Should().BeNull();
    }

    [Fact]
    public async Task ReadsTheCallersOrganisationWithTheFiltersItWasGiven()
    {
        await CreateTool().QueryAuditLogsAsync(
            eventType: "PermissionDenied",
            userId: "00000000-0000-0001-0000-000000000001",
            startTime: "2026-09-15T00:00:00Z",
            page: 2,
            pageSize: 10);

        _tenant.Verify(t => t.GetOrganizationAuditEventsAsync(
            OrgId,
            It.Is<string?>(q =>
                q!.Contains("eventType=PermissionDenied")
                && q.Contains("userId=00000000-0000-0001-0000-000000000001")
                && q.Contains("startDate=2026-09-15T00%3A00%3A00")
                && q.Contains("page=2")
                && q.Contains("pageSize=10")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 200)]
    public async Task PageSize_IsClampedToTheServiceLimit(int requested, int sent)
    {
        await CreateTool().QueryAuditLogsAsync(pageSize: requested);

        _tenant.Verify(t => t.GetOrganizationAuditEventsAsync(
            OrgId, It.Is<string?>(q => q!.Contains($"pageSize={sent}")), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A 403 means the caller lacks the role, and must not read like an outage. Collapsing the two
    /// is how #1641 sent an agent looking in the wrong place.
    /// </summary>
    [Fact]
    public async Task Forbidden_SaysWhichRoleIsNeeded_AndIsNotReportedAsAnOutage()
    {
        _tenant.Setup(t => t.GetOrganizationAuditEventsAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpStatusCode.Forbidden, (string?)null));

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Auditor");
        _availability.Verify(a => a.RecordFailure("Tenant", It.IsAny<Exception?>()), Times.Never);
    }

    [Fact]
    public async Task TenantUnreachable_ReturnsUnavailable()
    {
        _tenant.Setup(t => t.GetOrganizationAuditEventsAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvalidStartTime_ReturnsErrorWithoutReadingTheLog()
    {
        var result = await CreateTool().QueryAuditLogsAsync(startTime: "yesterday-ish");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("startTime");
        VerifyNoRead();
    }

    private void VerifyNoRead() =>
        _tenant.Verify(t => t.GetOrganizationAuditEventsAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── #1686: this is not a ledger transaction trail, and must say so ──

    /// <summary>
    /// Cold-start run #7 read the tool's name and description and expected a session that created
    /// a register, published a blueprint, created an instance and submitted three actions to show
    /// up here. It does not — this is the Tenant Service's admin/permission log — and the
    /// description is the one thing an agent reads BEFORE deciding whether calling this tool can
    /// answer its question at all.
    /// </summary>
    [Fact]
    public void Description_SaysThisIsNotALedgerTransactionTrail_AndNamesTheToolThatIs()
    {
        var description = typeof(AuditQueryTool)
            .GetMethod(nameof(AuditQueryTool.QueryAuditLogsAsync))!
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        description.Should().Contain("NOT a ledger transaction trail");
        description.Should().Contain("sorcha_transaction_history");
    }

    [Fact]
    public async Task NoMatchingEntries_MessageSaysThisIsNotEvidenceNothingHappened()
    {
        _tenant.Setup(t => t.GetOrganizationAuditEventsAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpStatusCode.OK, """{"events":[],"totalCount":0,"page":1,"pageSize":50}"""));

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("Success");
        result.Entries.Should().BeEmpty();
        result.Message.Should().Contain("sorcha_transaction_history");
        result.Message.Should().NotBe("No audit entries match this filter for your organisation.");
    }

    /// <summary>
    /// #1686: the id this tool reports (<c>PlatformUser</c>, cross-org) is a DIFFERENT id from the
    /// one <c>sorcha_user_list</c> reports for the same human (<c>UserIdentity</c>, org-scoped).
    /// Both used to be an unlabelled "UserId".
    /// </summary>
    [Fact]
    public async Task Entry_LabelsItsIdAsThePlatformUserId()
    {
        var result = await CreateTool().QueryAuditLogsAsync(eventType: "PermissionDenied");

        result.Entries[0].PlatformUserId.Should().Be("00000000-0000-0001-0000-000000000001");
    }
}
