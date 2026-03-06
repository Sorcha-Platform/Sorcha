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
/// Unit tests for operation status CLI command structure and argument parsing.
/// </summary>
public class OperationCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public OperationCommandTests()
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

    #region OperationCommand Structure Tests

    [Fact]
    public void OperationCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new OperationCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("operation");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void OperationCommand_ShouldHaveStatusSubcommand()
    {
        var command = new OperationCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(1);
        command.Subcommands.Should().Contain(c => c.Name == "status");
    }

    #endregion

    #region OperationStatusCommand Tests

    [Fact]
    public void OperationStatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new OperationStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("status");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void OperationStatusCommand_ShouldHaveRequiredOperationIdArgument()
    {
        var command = new OperationStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Arguments.Should().HaveCount(1);
        var arg = command.Arguments.First();
        arg.Name.Should().Be("operationId");
    }

    [Fact]
    public void OperationStatusCommand_ShouldParseArguments_WithOperationId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new OperationStatusCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("status op-12345-abcde");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void OperationStatusCommand_ShouldFailParsing_WithoutOperationId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new OperationStatusCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("status");
        parseResult.Errors.Should().NotBeEmpty();
    }

    #endregion
}
