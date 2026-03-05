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
/// Unit tests for Validator consent command structure and options.
/// </summary>
public class ValidatorConsentCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ValidatorConsentCommandTests()
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

    #region ValidatorConsentCommand Tests

    [Fact]
    public void ValidatorConsentCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorConsentCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("consent");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorConsentCommand_ShouldHaveFourSubcommands()
    {
        var command = new ValidatorConsentCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(4);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "pending", "approve", "reject", "refresh" });
    }

    #endregion

    #region ValidatorConsentPendingCommand Tests

    [Fact]
    public void ValidatorConsentPendingCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorConsentPendingCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("pending");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorConsentPendingCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorConsentPendingCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    #endregion

    #region ValidatorConsentApproveCommand Tests

    [Fact]
    public void ValidatorConsentApproveCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorConsentApproveCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("approve");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorConsentApproveCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorConsentApproveCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorConsentApproveCommand_ShouldHaveRequiredValidatorIdOption()
    {
        var command = new ValidatorConsentApproveCommand(_clientFactory, AuthService, ConfigService);
        var validatorIdOption = command.Options.FirstOrDefault(o => o.Name == "--validator-id");
        validatorIdOption.Should().NotBeNull();
        validatorIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorConsentApproveCommand_ShouldHaveOptionalYesOption()
    {
        var command = new ValidatorConsentApproveCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    #endregion

    #region ValidatorConsentRejectCommand Tests

    [Fact]
    public void ValidatorConsentRejectCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorConsentRejectCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("reject");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorConsentRejectCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorConsentRejectCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorConsentRejectCommand_ShouldHaveRequiredValidatorIdOption()
    {
        var command = new ValidatorConsentRejectCommand(_clientFactory, AuthService, ConfigService);
        var validatorIdOption = command.Options.FirstOrDefault(o => o.Name == "--validator-id");
        validatorIdOption.Should().NotBeNull();
        validatorIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void ValidatorConsentRejectCommand_ShouldHaveOptionalReasonOption()
    {
        var command = new ValidatorConsentRejectCommand(_clientFactory, AuthService, ConfigService);
        var reasonOption = command.Options.FirstOrDefault(o => o.Name == "--reason");
        reasonOption.Should().NotBeNull();
        reasonOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void ValidatorConsentRejectCommand_ShouldHaveOptionalYesOption()
    {
        var command = new ValidatorConsentRejectCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    #endregion

    #region ValidatorConsentRefreshCommand Tests

    [Fact]
    public void ValidatorConsentRefreshCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorConsentRefreshCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("refresh");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorConsentRefreshCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new ValidatorConsentRefreshCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    #endregion
}
