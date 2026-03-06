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
/// Unit tests for Validator command structure and options.
/// </summary>
public class ValidatorCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ValidatorCommandsTests()
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

    [Fact]
    public void ValidatorCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("validator");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new ValidatorCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(8);
        command.Subcommands.Select(c => c.Name).Should().Contain(
            new[] { "status", "start", "stop", "process", "integrity-check", "consent", "metrics", "threshold" });
    }

    #region ValidatorStatusCommand Tests

    [Fact]
    public void ValidatorStatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("status");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region ValidatorStartCommand Tests

    [Fact]
    public void ValidatorStartCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorStartCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("start");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region ValidatorStopCommand Tests

    [Fact]
    public void ValidatorStopCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorStopCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("stop");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorStopCommand_ShouldHaveOptionalYesOption()
    {
        var command = new ValidatorStopCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    #endregion

    #region ValidatorProcessCommand Tests

    [Fact]
    public void ValidatorProcessCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorProcessCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("process");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorProcessCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorProcessCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion

    #region ValidatorIntegrityCheckCommand Tests

    [Fact]
    public void ValidatorIntegrityCheckCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorIntegrityCheckCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("integrity-check");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorIntegrityCheckCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorIntegrityCheckCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion
}
