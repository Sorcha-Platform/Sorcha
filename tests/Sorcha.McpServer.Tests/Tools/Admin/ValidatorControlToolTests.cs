// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 4: ValidatorControlTool starts/stops/restarts a register's validator via
/// the typed <see cref="IValidatorServiceClient"/> (platform tier + admin role). Restart is a
/// mempool-persisting stop followed by a start.
/// </summary>
public class ValidatorControlToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IValidatorServiceClient> _validatorClientMock = new();

    private ValidatorControlTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _validatorClientMock.Object,
        Mock.Of<ILogger<ValidatorControlTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_control")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(true);
    }

    [Fact]
    public async Task InvokeAsync_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_control")).Returns(false);

        var result = await CreateTool().InvokeAsync("start", "reg-1");

        result.Status.Should().Be("Unauthorized");
        _validatorClientMock.Verify(c => c.StartValidatorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_InvalidAction_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_control")).Returns(true);

        var result = await CreateTool().InvokeAsync("pause", "reg-1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_MissingRegisterId_ReturnsError()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_control")).Returns(true);

        var result = await CreateTool().InvokeAsync("start", "");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_validator_control")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Validator")).Returns(false);

        var result = await CreateTool().InvokeAsync("start", "reg-1");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task InvokeAsync_Start_Success()
    {
        Allow();
        _validatorClientMock.Setup(c => c.StartValidatorAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateTool().InvokeAsync("start", "reg-1");

        result.Status.Should().Be("Success");
        result.Action.Should().Be("start");
        result.RegisterId.Should().Be("reg-1");
        _validatorClientMock.Verify(c => c.StartValidatorAsync("reg-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_Stop_PassesPersistFlag()
    {
        Allow();
        _validatorClientMock.Setup(c => c.StopValidatorAsync("reg-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateTool().InvokeAsync("stop", "reg-1", persistMemPool: false);

        result.Status.Should().Be("Success");
        result.Action.Should().Be("stop");
        _validatorClientMock.Verify(c => c.StopValidatorAsync("reg-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_Restart_StopsThenStarts()
    {
        Allow();
        _validatorClientMock.Setup(c => c.StopValidatorAsync("reg-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _validatorClientMock.Setup(c => c.StartValidatorAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateTool().InvokeAsync("restart", "reg-1");

        result.Status.Should().Be("Success");
        result.Action.Should().Be("restart");
        _validatorClientMock.Verify(c => c.StopValidatorAsync("reg-1", true, It.IsAny<CancellationToken>()), Times.Once);
        _validatorClientMock.Verify(c => c.StartValidatorAsync("reg-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_Restart_StopFails_AbortsWithoutStart()
    {
        Allow();
        _validatorClientMock.Setup(c => c.StopValidatorAsync("reg-1", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateTool().InvokeAsync("restart", "reg-1");

        result.Status.Should().Be("Error");
        _validatorClientMock.Verify(c => c.StartValidatorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_StartNotAccepted_ReturnsError()
    {
        Allow();
        _validatorClientMock.Setup(c => c.StartValidatorAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateTool().InvokeAsync("start", "reg-1");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsTimeout()
    {
        Allow();
        _validatorClientMock.Setup(c => c.StartValidatorAsync("reg-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var result = await CreateTool().InvokeAsync("start", "reg-1");

        result.Status.Should().Be("Timeout");
    }
}
