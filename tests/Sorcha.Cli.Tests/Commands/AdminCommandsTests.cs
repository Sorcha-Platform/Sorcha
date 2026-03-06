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
/// Unit tests for Admin command structure and options.
/// </summary>
public class AdminCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public AdminCommandsTests()
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
    public void AdminCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AdminCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("admin");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AdminCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new AdminCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(3);
        command.Subcommands.Select(c => c.Name).Should().Contain(
            new[] { "health", "alerts", "events" });
    }

    #region AdminHealthCommand Tests

    [Fact]
    public void AdminHealthCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AdminHealthCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("health");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region AdminAlertsCommand Tests

    [Fact]
    public void AdminAlertsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AdminAlertsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("alerts");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AdminAlertsCommand_ShouldHaveOptionalSeverityOption()
    {
        var command = new AdminAlertsCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--severity");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion
}
