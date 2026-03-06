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
/// Unit tests for wallet access delegation command structure and argument parsing.
/// </summary>
public class WalletAccessCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public WalletAccessCommandTests()
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

    #region WalletAccessCommand Structure Tests

    [Fact]
    public void WalletAccessCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new WalletAccessCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("access");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WalletAccessCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new WalletAccessCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(4);
        command.Subcommands.Select(c => c.Name).Should()
            .Contain(new[] { "grant", "list", "revoke", "check" });
    }

    #endregion

    #region WalletAccessGrantCommand Tests

    [Fact]
    public void WalletAccessGrantCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new WalletAccessGrantCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("grant");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WalletAccessGrantCommand_ShouldHaveRequiredOptions()
    {
        var command = new WalletAccessGrantCommand(_clientFactory, AuthService, ConfigService);

        var addressOption = command.Options.FirstOrDefault(o => o.Name == "--address");
        addressOption.Should().NotBeNull();
        addressOption!.Required.Should().BeTrue();

        var subjectOption = command.Options.FirstOrDefault(o => o.Name == "--subject");
        subjectOption.Should().NotBeNull();
        subjectOption!.Required.Should().BeTrue();

        var rightOption = command.Options.FirstOrDefault(o => o.Name == "--right");
        rightOption.Should().NotBeNull();
        rightOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void WalletAccessGrantCommand_ShouldHaveOptionalReasonOption()
    {
        var command = new WalletAccessGrantCommand(_clientFactory, AuthService, ConfigService);
        var reasonOption = command.Options.FirstOrDefault(o => o.Name == "--reason");
        reasonOption.Should().NotBeNull();
        reasonOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void WalletAccessGrantCommand_ShouldParseArguments_WithRequiredOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new WalletAccessGrantCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("grant --address wallet-123 --subject user-456 --right ReadWrite");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WalletAccessGrantCommand_ShouldParseArguments_WithAllOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new WalletAccessGrantCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("grant --address wallet-123 --subject user-456 --right ReadWrite --reason \"Needs signing access\"");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region WalletAccessListCommand Tests

    [Fact]
    public void WalletAccessListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new WalletAccessListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WalletAccessListCommand_ShouldHaveRequiredAddressOption()
    {
        var command = new WalletAccessListCommand(_clientFactory, AuthService, ConfigService);
        var addressOption = command.Options.FirstOrDefault(o => o.Name == "--address");
        addressOption.Should().NotBeNull();
        addressOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void WalletAccessListCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new WalletAccessListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list --address wallet-123");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region WalletAccessRevokeCommand Tests

    [Fact]
    public void WalletAccessRevokeCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new WalletAccessRevokeCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("revoke");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WalletAccessRevokeCommand_ShouldHaveRequiredOptions()
    {
        var command = new WalletAccessRevokeCommand(_clientFactory, AuthService, ConfigService);

        var addressOption = command.Options.FirstOrDefault(o => o.Name == "--address");
        addressOption.Should().NotBeNull();
        addressOption!.Required.Should().BeTrue();

        var subjectOption = command.Options.FirstOrDefault(o => o.Name == "--subject");
        subjectOption.Should().NotBeNull();
        subjectOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void WalletAccessRevokeCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new WalletAccessRevokeCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("revoke --address wallet-123 --subject user-456");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region WalletAccessCheckCommand Tests

    [Fact]
    public void WalletAccessCheckCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new WalletAccessCheckCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("check");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void WalletAccessCheckCommand_ShouldHaveRequiredOptions()
    {
        var command = new WalletAccessCheckCommand(_clientFactory, AuthService, ConfigService);

        var addressOption = command.Options.FirstOrDefault(o => o.Name == "--address");
        addressOption.Should().NotBeNull();
        addressOption!.Required.Should().BeTrue();

        var subjectOption = command.Options.FirstOrDefault(o => o.Name == "--subject");
        subjectOption.Should().NotBeNull();
        subjectOption!.Required.Should().BeTrue();

        var rightOption = command.Options.FirstOrDefault(o => o.Name == "--right");
        rightOption.Should().NotBeNull();
        rightOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void WalletAccessCheckCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new WalletAccessCheckCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("check --address wallet-123 --subject user-456 --right ReadOnly");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion
}
