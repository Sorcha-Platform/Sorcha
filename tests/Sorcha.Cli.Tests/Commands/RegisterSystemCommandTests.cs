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
/// Unit tests for RegisterSystemCommand structure and options.
/// </summary>
public class RegisterSystemCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public RegisterSystemCommandTests()
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

    #region RegisterSystemCommand (Parent Group) Tests

    [Fact]
    public void RegisterSystemCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterSystemCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("system");
    }

    [Fact]
    public void RegisterSystemCommand_ShouldHaveDescription()
    {
        var command = new RegisterSystemCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterSystemCommand_ShouldHaveTwoSubcommands()
    {
        var command = new RegisterSystemCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(2);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "status", "blueprints" });
    }

    #endregion

    #region RegisterSystemStatusCommand Tests

    [Fact]
    public void RegisterSystemStatusCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterSystemStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("status");
    }

    [Fact]
    public void RegisterSystemStatusCommand_ShouldHaveDescription()
    {
        var command = new RegisterSystemStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region RegisterSystemBlueprintsCommand Tests

    [Fact]
    public void RegisterSystemBlueprintsCommand_ShouldHaveCorrectName()
    {
        var command = new RegisterSystemBlueprintsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("blueprints");
    }

    [Fact]
    public void RegisterSystemBlueprintsCommand_ShouldHaveDescription()
    {
        var command = new RegisterSystemBlueprintsCommand(_clientFactory, AuthService, ConfigService);
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterSystemBlueprintsCommand_ShouldHaveOptionalPageOption()
    {
        var command = new RegisterSystemBlueprintsCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--page");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterSystemBlueprintsCommand_ShouldHaveOptionalPageSizeOption()
    {
        var command = new RegisterSystemBlueprintsCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--page-size");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion
}
