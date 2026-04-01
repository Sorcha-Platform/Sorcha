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
/// Unit tests for Blueprint command structure and options.
/// </summary>
public class BlueprintCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public BlueprintCommandsTests()
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
    public void BlueprintCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("blueprint");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintCommand_ShouldHaveSevenSubcommands()
    {
        var command = new BlueprintCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(8);
        command.Subcommands.Select(c => c.Name).Should().Contain(
            new[] { "list", "get", "create", "publish", "delete", "versions", "instances", "export" });
    }

    #region BlueprintListCommand Tests

    [Fact]
    public void BlueprintListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region BlueprintGetCommand Tests

    [Fact]
    public void BlueprintGetCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintGetCommand_ShouldHaveRequiredIdOption()
    {
        var command = new BlueprintGetCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion

    #region BlueprintCreateCommand Tests

    [Fact]
    public void BlueprintCreateCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintCreateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("create");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintCreateCommand_ShouldHaveRequiredFileOption()
    {
        var command = new BlueprintCreateCommand(_clientFactory, AuthService, ConfigService);
        var fileOption = command.Options.FirstOrDefault(o => o.Name == "--file");
        fileOption.Should().NotBeNull();
        fileOption!.Required.Should().BeTrue();
    }

    #endregion

    #region BlueprintPublishCommand Tests

    [Fact]
    public void BlueprintPublishCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintPublishCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("publish");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintPublishCommand_ShouldHaveRequiredIdOption()
    {
        var command = new BlueprintPublishCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void BlueprintPublishCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new BlueprintPublishCommand(_clientFactory, AuthService, ConfigService);
        var regOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        regOption.Should().NotBeNull();
        regOption!.Required.Should().BeTrue();
    }

    #endregion

    #region BlueprintDeleteCommand Tests

    [Fact]
    public void BlueprintDeleteCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintDeleteCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("delete");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintDeleteCommand_ShouldHaveRequiredIdOption()
    {
        var command = new BlueprintDeleteCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void BlueprintDeleteCommand_ShouldHaveOptionalYesOption()
    {
        var command = new BlueprintDeleteCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    #endregion

    #region BlueprintVersionsCommand Tests

    [Fact]
    public void BlueprintVersionsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintVersionsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("versions");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintVersionsCommand_ShouldHaveRequiredIdOption()
    {
        var command = new BlueprintVersionsCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion

    #region BlueprintInstancesCommand Tests

    [Fact]
    public void BlueprintInstancesCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new BlueprintInstancesCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("instances");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BlueprintInstancesCommand_ShouldHaveOptionalBlueprintIdOption()
    {
        var command = new BlueprintInstancesCommand(_clientFactory, AuthService, ConfigService);
        var bpOption = command.Options.FirstOrDefault(o => o.Name == "--blueprint-id");
        bpOption.Should().NotBeNull();
        bpOption!.Required.Should().BeFalse();
    }

    #endregion
}
