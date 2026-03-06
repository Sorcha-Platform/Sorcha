// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using FluentAssertions;
using Moq;
using Sorcha.Cli.Commands;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Xunit;

namespace Sorcha.Cli.Tests.Commands;

/// <summary>
/// Unit tests for Participant suspend and reactivate command structure and options.
/// </summary>
public class ParticipantSuspendCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ParticipantSuspendCommandTests()
    {
        _mockAuthService = new Mock<IAuthenticationService>();
        _mockConfigService = new Mock<IConfigurationService>();

        // Setup default mock behavior
        _mockConfigService.Setup(x => x.GetActiveProfileAsync())
            .ReturnsAsync(new Profile { Name = "test" });
        _mockAuthService.Setup(x => x.GetAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync("test-token");

        _clientFactory = new HttpClientFactory(_mockConfigService.Object);
    }

    private IAuthenticationService AuthService => _mockAuthService.Object;
    private IConfigurationService ConfigService => _mockConfigService.Object;

    #region ParticipantSuspendCommand Tests

    [Fact]
    public void ParticipantSuspendCommand_ShouldHaveCorrectName()
    {
        var command = new ParticipantSuspendCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("suspend");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantSuspendCommand_ShouldHaveRequiredOptions()
    {
        var command = new ParticipantSuspendCommand(_clientFactory, AuthService, ConfigService);

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion

    #region ParticipantReactivateCommand Tests

    [Fact]
    public void ParticipantReactivateCommand_ShouldHaveCorrectName()
    {
        var command = new ParticipantReactivateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("reactivate");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantReactivateCommand_ShouldHaveRequiredOptions()
    {
        var command = new ParticipantReactivateCommand(_clientFactory, AuthService, ConfigService);

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion
}
