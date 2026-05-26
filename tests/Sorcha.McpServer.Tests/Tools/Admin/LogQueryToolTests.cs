// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4 (LOCKED DECISION): no log-query API exists, so LogQueryTool is marked
/// NotSupported. It keeps its admin auth gate but otherwise fails honestly.
/// </summary>
public class LogQueryToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();

    private LogQueryTool CreateTool() => new(
        _authServiceMock.Object,
        Mock.Of<ILogger<LogQueryTool>>());

    [Fact]
    public async Task QueryLogsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_log_query")).Returns(false);

        var result = await CreateTool().QueryLogsAsync();

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task QueryLogsAsync_Authorized_ReturnsNotSupported()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_log_query")).Returns(true);

        var result = await CreateTool().QueryLogsAsync();

        result.Status.Should().Be("NotSupported");
        result.Message.Should().Contain("no log-query API");
        result.Entries.Should().BeEmpty();
    }
}
