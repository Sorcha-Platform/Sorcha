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
/// Unit tests for RegisterPolicyCommand structure and options.
/// </summary>
public class RegisterPolicyCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public RegisterPolicyCommandTests()
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

    #region RegisterPolicyCommand (Parent Group) Tests

    [Fact]
    public void RegisterPolicyCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterPolicyCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("policy");
    }

    [Fact]
    public void RegisterPolicyCommand_ShouldHaveDescription()
    {
        var command = new RegisterPolicyCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterPolicyCommand_ShouldHaveThreeSubcommands()
    {
        var command = new RegisterPolicyCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(3);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "get", "history", "update" });
    }

    #endregion

    #region RegisterPolicyGetCommand Tests

    [Fact]
    public void RegisterPolicyGetCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterPolicyGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
    }

    [Fact]
    public void RegisterPolicyGetCommand_ShouldHaveDescription()
    {
        var command = new RegisterPolicyGetCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterPolicyGetCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new RegisterPolicyGetCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion

    #region RegisterPolicyHistoryCommand Tests

    [Fact]
    public void RegisterPolicyHistoryCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterPolicyHistoryCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("history");
    }

    [Fact]
    public void RegisterPolicyHistoryCommand_ShouldHaveDescription()
    {
        var command = new RegisterPolicyHistoryCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterPolicyHistoryCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new RegisterPolicyHistoryCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterPolicyHistoryCommand_ShouldHaveOptionalPageOption()
    {
        var command = new RegisterPolicyHistoryCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--page");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterPolicyHistoryCommand_ShouldHaveOptionalPageSizeOption()
    {
        var command = new RegisterPolicyHistoryCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--page-size");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion

    #region RegisterPolicyUpdateCommand Tests

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("update");
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveDescription()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveOptionalMinValidatorsOption()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--min-validators");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveOptionalMaxValidatorsOption()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--max-validators");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveOptionalThresholdOption()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--signature-threshold");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveOptionalRegistrationModeOption()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--registration-mode");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterPolicyUpdateCommand_ShouldHaveOptionalYesOption()
    {
        var command = new RegisterPolicyUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--yes");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion
}
