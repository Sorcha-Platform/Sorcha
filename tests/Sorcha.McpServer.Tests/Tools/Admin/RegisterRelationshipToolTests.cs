// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 1: RegisterRelationshipTool routes through the typed <see cref="IRegisterServiceClient"/>.
/// </summary>
public class RegisterRelationshipToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();

    private RegisterRelationshipTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _registerClientMock.Object,
        Mock.Of<ILogger<RegisterRelationshipTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_relationship")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Register")).Returns(true);
    }

    [Fact]
    public async Task GetRelationshipAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_relationship")).Returns(false);

        var result = await CreateTool().GetRelationshipAsync("reg-1");

        result.Status.Should().Be("Unauthorized");
        result.Relationship.Should().BeNull();
    }

    [Fact]
    public async Task GetRelationshipAsync_EmptyRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_register_relationship")).Returns(true);

        var result = await CreateTool().GetRelationshipAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task GetRelationshipAsync_ClientReturnsNull_ReturnsNotFound()
    {
        Allow();
        _registerClientMock
            .Setup(c => c.GetLocalRelationshipAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegisterLocalRelationship?)null);

        var result = await CreateTool().GetRelationshipAsync("reg-1");

        result.Status.Should().Be("NotFound");
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Register"), Times.Once);
    }

    [Fact]
    public async Task GetRelationshipAsync_Success_ReturnsRelationship()
    {
        Allow();
        var relationship = new RegisterLocalRelationship(
            "reg-1", RegisterRoleSet.Owner | RegisterRoleSet.Validator, 5, DateTimeOffset.UtcNow);
        _registerClientMock
            .Setup(c => c.GetLocalRelationshipAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationship);

        var result = await CreateTool().GetRelationshipAsync("reg-1");

        result.Status.Should().Be("Success");
        result.Relationship.Should().NotBeNull();
        result.Relationship!.IsOwner.Should().BeTrue();
        result.Relationship.IsValidator.Should().BeTrue();
    }
}
