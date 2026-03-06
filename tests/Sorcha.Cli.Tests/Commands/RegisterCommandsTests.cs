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
/// Unit tests for Register command structure and options.
/// </summary>
public class RegisterCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public RegisterCommandsTests()
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
    public void RegisterCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("register");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new RegisterCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(8);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "list", "get", "create", "delete", "update", "stats", "policy", "system" });
    }

    #region RegisterListCommand Tests

    [Fact]
    public void RegisterListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region RegisterGetCommand Tests

    [Fact]
    public void RegisterGetCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterGetCommand_ShouldHaveRequiredIdOption()
    {
        var command = new RegisterGetCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion

    #region RegisterCreateCommand Tests

    [Fact]
    public void RegisterCreateCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterCreateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("create");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterCreateCommand_ShouldHaveRequiredNameOption()
    {
        var command = new RegisterCreateCommand(_clientFactory, AuthService, ConfigService);
        var nameOption = command.Options.FirstOrDefault(o => o.Name == "--name");
        nameOption.Should().NotBeNull();
        nameOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreateCommand_ShouldHaveRequiredTenantIdOption()
    {
        var command = new RegisterCreateCommand(_clientFactory, AuthService, ConfigService);
        var tenantIdOption = command.Options.FirstOrDefault(o => o.Name == "--tenant-id");
        tenantIdOption.Should().NotBeNull();
        tenantIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreateCommand_ShouldHaveRequiredOwnerWalletOption()
    {
        var command = new RegisterCreateCommand(_clientFactory, AuthService, ConfigService);
        var ownerWalletOption = command.Options.FirstOrDefault(o => o.Name == "--owner-wallet");
        ownerWalletOption.Should().NotBeNull();
        ownerWalletOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterCreateCommand_ShouldHaveOptionalDescriptionOption()
    {
        var command = new RegisterCreateCommand(_clientFactory, AuthService, ConfigService);
        var descOption = command.Options.FirstOrDefault(o => o.Name == "--description");
        descOption.Should().NotBeNull();
        descOption!.Required.Should().BeFalse();
    }

    #endregion

    #region RegisterDeleteCommand Tests

    [Fact]
    public void RegisterDeleteCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterDeleteCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("delete");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterDeleteCommand_ShouldHaveRequiredIdOption()
    {
        var command = new RegisterDeleteCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterDeleteCommand_ShouldHaveOptionalYesOption()
    {
        var command = new RegisterDeleteCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    #endregion

    #region RegisterUpdateCommand Tests

    [Fact]
    public void RegisterUpdateCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterUpdateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("update");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RegisterUpdateCommand_ShouldHaveRequiredIdOption()
    {
        var command = new RegisterUpdateCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void RegisterUpdateCommand_ShouldHaveOptionalNameOption()
    {
        var command = new RegisterUpdateCommand(_clientFactory, AuthService, ConfigService);
        var nameOption = command.Options.FirstOrDefault(o => o.Name == "--name");
        nameOption.Should().NotBeNull();
        nameOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterUpdateCommand_ShouldHaveOptionalStatusOption()
    {
        var command = new RegisterUpdateCommand(_clientFactory, AuthService, ConfigService);
        var statusOption = command.Options.FirstOrDefault(o => o.Name == "--status");
        statusOption.Should().NotBeNull();
        statusOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void RegisterUpdateCommand_ShouldHaveOptionalAdvertiseOption()
    {
        var command = new RegisterUpdateCommand(_clientFactory, AuthService, ConfigService);
        var advertiseOption = command.Options.FirstOrDefault(o => o.Name == "--advertise");
        advertiseOption.Should().NotBeNull();
        advertiseOption!.Required.Should().BeFalse();
    }

    #endregion

    #region RegisterStatsCommand Tests

    [Fact]
    public void RegisterStatsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new RegisterStatsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("stats");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion
}
