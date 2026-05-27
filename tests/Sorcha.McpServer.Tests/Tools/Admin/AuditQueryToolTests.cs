// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4 (LOCKED DECISION): no audit-query API exists, so AuditQueryTool is marked
/// NotSupported. It keeps its admin auth gate but otherwise fails honestly.
/// </summary>
public class AuditQueryToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();

    private AuditQueryTool CreateTool() => new(
        _authServiceMock.Object,
        Mock.Of<ILogger<AuditQueryTool>>());

    [Fact]
    public async Task QueryAuditLogsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_audit_query")).Returns(false);

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task QueryAuditLogsAsync_Authorized_ReturnsNotSupported()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_audit_query")).Returns(true);

        var result = await CreateTool().QueryAuditLogsAsync();

        result.Status.Should().Be("NotSupported");
        result.Message.Should().Contain("no audit-query API");
        result.Entries.Should().BeEmpty();
    }
}
