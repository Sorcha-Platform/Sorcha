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
/// Unit tests for Validator threshold command structure and options.
/// </summary>
public class ValidatorThresholdCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ValidatorThresholdCommandTests()
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

    #region ValidatorThresholdCommand Tests

    [Fact]
    public void ValidatorThresholdCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorThresholdCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("threshold");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorThresholdCommand_ShouldHaveTwoSubcommands()
    {
        var command = new ValidatorThresholdCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(2);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "status", "setup" });
    }

    #endregion

    #region ValidatorThresholdStatusCommand Tests

    [Fact]
    public void ValidatorThresholdStatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorThresholdStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("status");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorThresholdStatusCommand_ShouldHaveNoOptions()
    {
        var command = new ValidatorThresholdStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Options.Should().BeEmpty();
    }

    #endregion

    #region ValidatorThresholdSetupCommand Tests

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("setup");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveRequiredThresholdOption()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        var thresholdOption = command.Options.FirstOrDefault(o => o.Name == "--threshold");
        thresholdOption.Should().NotBeNull();
        thresholdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveRequiredTotalValidatorsOption()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        var totalValidatorsOption = command.Options.FirstOrDefault(o => o.Name == "--total-validators");
        totalValidatorsOption.Should().NotBeNull();
        totalValidatorsOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveRequiredValidatorIdsOption()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        var validatorIdsOption = command.Options.FirstOrDefault(o => o.Name == "--validator-ids");
        validatorIdsOption.Should().NotBeNull();
        validatorIdsOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveOptionalYesOption()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void ValidatorThresholdSetupCommand_ShouldHaveFiveOptions()
    {
        var command = new ValidatorThresholdSetupCommand(_clientFactory, AuthService, ConfigService);
        command.Options.Should().HaveCount(5);
    }

    #endregion
}
