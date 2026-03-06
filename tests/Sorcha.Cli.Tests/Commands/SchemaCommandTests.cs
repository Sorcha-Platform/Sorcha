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
/// Unit tests for schema provider CLI command structure and argument parsing.
/// </summary>
public class SchemaCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public SchemaCommandTests()
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

    #region SchemaCommand Structure Tests

    [Fact]
    public void SchemaCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new SchemaCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("schema");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SchemaCommand_ShouldHaveProvidersSubcommand()
    {
        var command = new SchemaCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(1);
        command.Subcommands.Should().Contain(c => c.Name == "providers");
    }

    #endregion

    #region SchemaProvidersCommand Structure Tests

    [Fact]
    public void SchemaProvidersCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new SchemaProvidersCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("providers");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SchemaProvidersCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new SchemaProvidersCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(2);
        command.Subcommands.Select(c => c.Name).Should()
            .Contain(new[] { "list", "refresh" });
    }

    #endregion

    #region SchemaProvidersListCommand Tests

    [Fact]
    public void SchemaProvidersListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new SchemaProvidersListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SchemaProvidersListCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new SchemaProvidersListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region SchemaProvidersRefreshCommand Tests

    [Fact]
    public void SchemaProvidersRefreshCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new SchemaProvidersRefreshCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("refresh");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SchemaProvidersRefreshCommand_ShouldHaveRequiredNameOption()
    {
        var command = new SchemaProvidersRefreshCommand(_clientFactory, AuthService, ConfigService);
        var nameOption = command.Options.FirstOrDefault(o => o.Name == "--name");
        nameOption.Should().NotBeNull();
        nameOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void SchemaProvidersRefreshCommand_ShouldParseArguments_WithRequiredName()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new SchemaProvidersRefreshCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("refresh --name json-schema-store");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SchemaProvidersRefreshCommand_ShouldFailParsing_WithoutRequiredName()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new SchemaProvidersRefreshCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("refresh");
        parseResult.Errors.Should().NotBeEmpty();
    }

    #endregion
}
