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
/// Unit tests for admin events CLI command structure and argument parsing.
/// </summary>
public class EventAdminCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public EventAdminCommandTests()
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

    #region AdminEventsCommand Structure Tests

    [Fact]
    public void AdminEventsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AdminEventsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("events");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AdminEventsCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new AdminEventsCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(2);
        command.Subcommands.Select(c => c.Name).Should()
            .Contain(new[] { "list", "delete" });
    }

    #endregion

    #region AdminEventsListCommand Tests

    [Fact]
    public void AdminEventsListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AdminEventsListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AdminEventsListCommand_ShouldHaveOptionalSeverityOption()
    {
        var command = new AdminEventsListCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--severity");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void AdminEventsListCommand_ShouldHaveOptionalSinceOption()
    {
        var command = new AdminEventsListCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--since");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void AdminEventsListCommand_ShouldHaveOptionalPageOption()
    {
        var command = new AdminEventsListCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--page");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void AdminEventsListCommand_ShouldParseArguments_NoOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AdminEventsListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AdminEventsListCommand_ShouldParseArguments_WithSeverityFilter()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AdminEventsListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list --severity Warning");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AdminEventsListCommand_ShouldParseArguments_WithAllOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AdminEventsListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list --severity Error --since 2026-01-01 --page 2");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region AdminEventsDeleteCommand Tests

    [Fact]
    public void AdminEventsDeleteCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new AdminEventsDeleteCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("delete");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AdminEventsDeleteCommand_ShouldHaveRequiredIdOption()
    {
        var command = new AdminEventsDeleteCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void AdminEventsDeleteCommand_ShouldParseArguments_WithRequiredId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AdminEventsDeleteCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("delete --id evt-12345");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AdminEventsDeleteCommand_ShouldFailParsing_WithoutRequiredId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new AdminEventsDeleteCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("delete");
        parseResult.Errors.Should().NotBeEmpty();
    }

    #endregion
}
