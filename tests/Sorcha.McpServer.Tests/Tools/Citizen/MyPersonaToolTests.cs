// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Citizen;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tests.Tools.Citizen;

/// <summary>
/// Feature 140 Wave 3: MyPersonaTool reads (no body) or replaces (with body) the caller's
/// own persona via the typed <see cref="ITenantServiceClient"/> (consumer tier).
/// </summary>
public class MyPersonaToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<ITenantServiceClient> _tenantClientMock = new();

    private MyPersonaTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _tenantClientMock.Object,
        Mock.Of<ILogger<MyPersonaTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_persona")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Tenant")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_my_persona")).Returns(false);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Unauthorized");
        _tenantClientMock.Verify(c => c.GetMyPersonaAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_NoBody_ReadsPersona()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetMyPersonaAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"givenName\":null}");

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Success");
        result.Updated.Should().BeFalse();
        result.Persona.Should().Contain("givenName");
        _tenantClientMock.Verify(c => c.GetMyPersonaAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _tenantClientMock.Verify(c => c.ReplaceMyPersonaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WithBody_ReplacesPersona()
    {
        Allow();
        const string body = "{\"givenName\":\"Ada\"}";
        _tenantClientMock.Setup(c => c.ReplaceMyPersonaAsync(body, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"givenName\":{\"value\":\"Ada\"}}");

        var result = await CreateTool().InvokeAsync(body);

        result.Status.Should().Be("Success");
        result.Updated.Should().BeTrue();
        _tenantClientMock.Verify(c => c.ReplaceMyPersonaAsync(body, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WithContext_BuildsQueryString()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetMyPersonaAsync("context=org-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");

        var result = await CreateTool().InvokeAsync(context: "org-1");

        result.Status.Should().Be("Success");
        _tenantClientMock.Verify(c => c.GetMyPersonaAsync("context=org-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_NullBody_ReturnsError()
    {
        Allow();
        _tenantClientMock.Setup(c => c.GetMyPersonaAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().InvokeAsync();

        result.Status.Should().Be("Error");
        _availabilityTrackerMock.Verify(a => a.RecordFailure("Tenant"), Times.Once);
    }
}
