// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using FluentAssertions;
using Moq;
using Sorcha.Cli.Commands;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Xunit;

namespace Sorcha.Cli.Tests.Commands;

/// <summary>
/// Unit tests for action CLI command structure and option parsing.
/// </summary>
public class ActionCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ActionCommandTests()
    {
        _mockAuthService = new Mock<IAuthenticationService>();
        _mockConfigService = new Mock<IConfigurationService>();

        _mockConfigService.Setup(x => x.GetActiveProfileAsync())
            .ReturnsAsync(new Profile { Name = "test" });
        _mockConfigService.Setup(x => x.GetProfileAsync(It.IsAny<string>()))
            .ReturnsAsync(new Profile { Name = "test", ServiceUrl = "http://localhost:80" });
        _mockAuthService.Setup(x => x.GetAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync("test-token");

        _clientFactory = new HttpClientFactory(_mockConfigService.Object);
    }

    private IAuthenticationService AuthService => _mockAuthService.Object;
    private IConfigurationService ConfigService => _mockConfigService.Object;

    #region ActionCommand Structure Tests

    [Fact]
    public void ActionCommand_HasExecuteSubcommand()
    {
        var command = new ActionCommand(_clientFactory, AuthService, ConfigService);

        command.Name.Should().Be("action");
        command.Description.Should().NotBeNullOrWhiteSpace();
        command.Subcommands.Should().ContainSingle(s => s.Name == "execute");
    }

    #endregion

    #region ActionExecuteCommand Tests

    [Fact]
    public void ActionExecuteCommand_RequiredOptions_ArePresent()
    {
        var command = new ActionExecuteCommand(_clientFactory, AuthService, ConfigService);

        command.Options.Should().Contain(o => o.Name == "--blueprint");
        command.Options.Should().Contain(o => o.Name == "--action");
        command.Options.Should().Contain(o => o.Name == "--instance");
        command.Options.Should().Contain(o => o.Name == "--wallet");
        command.Options.Should().Contain(o => o.Name == "--register");

        var blueprintOpt = command.Options.First(o => o.Name == "--blueprint");
        blueprintOpt.Required.Should().BeTrue();

        var actionOpt = command.Options.First(o => o.Name == "--action");
        actionOpt.Required.Should().BeTrue();

        var instanceOpt = command.Options.First(o => o.Name == "--instance");
        instanceOpt.Required.Should().BeTrue();

        var walletOpt = command.Options.First(o => o.Name == "--wallet");
        walletOpt.Required.Should().BeTrue();

        var registerOpt = command.Options.First(o => o.Name == "--register");
        registerOpt.Required.Should().BeTrue();
    }

    [Fact]
    public void ActionExecuteCommand_NoWaitOption_IsOptional()
    {
        var command = new ActionExecuteCommand(_clientFactory, AuthService, ConfigService);

        var noWaitOpt = command.Options.FirstOrDefault(o => o.Name == "--no-wait");
        noWaitOpt.Should().NotBeNull();
        noWaitOpt!.Required.Should().BeFalse();
    }

    [Fact]
    public void ActionExecuteCommand_PayloadOption_IsOptional()
    {
        var command = new ActionExecuteCommand(_clientFactory, AuthService, ConfigService);

        var payloadOpt = command.Options.FirstOrDefault(o => o.Name == "--payload");
        payloadOpt.Should().NotBeNull();
        payloadOpt!.Required.Should().BeFalse();
    }

    #endregion
}
