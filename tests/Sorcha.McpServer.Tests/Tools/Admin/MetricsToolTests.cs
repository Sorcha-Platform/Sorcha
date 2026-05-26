// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Spec 139 US4 (LOCKED DECISION): no metrics-query API exists, so MetricsTool is marked
/// NotSupported. It keeps its admin auth gate but otherwise fails honestly.
/// </summary>
public class MetricsToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();

    private MetricsTool CreateTool() => new(
        _authServiceMock.Object,
        Mock.Of<ILogger<MetricsTool>>());

    [Fact]
    public async Task GetMetricsAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_metrics")).Returns(false);

        var result = await CreateTool().GetMetricsAsync();

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task GetMetricsAsync_Authorized_ReturnsNotSupported()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_metrics")).Returns(true);

        var result = await CreateTool().GetMetricsAsync();

        result.Status.Should().Be("NotSupported");
        result.Message.Should().Contain("no metrics-query API");
        result.Services.Should().BeEmpty();
    }
}
