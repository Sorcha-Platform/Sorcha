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
/// Unit tests for Participant command structure and options.
/// </summary>
public class ParticipantCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ParticipantCommandsTests()
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
    public void ParticipantCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("participant");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantCommand_ShouldHaveTenSubcommands()
    {
        var command = new ParticipantCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(10);
        command.Subcommands.Select(c => c.Name).Should().Contain(
            new[] { "register", "list", "get", "update", "search", "wallet-link", "suspend", "reactivate", "publish", "unpublish" });
    }

    #region ParticipantRegisterCommand Tests

    [Fact]
    public void ParticipantRegisterCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantRegisterCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("register");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantRegisterCommand_ShouldHaveRequiredOrgIdOption()
    {
        var command = new ParticipantRegisterCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantRegisterCommand_ShouldHaveRequiredUserIdOption()
    {
        var command = new ParticipantRegisterCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--user-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantRegisterCommand_ShouldHaveRequiredDisplayNameOption()
    {
        var command = new ParticipantRegisterCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--display-name");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion

    #region ParticipantListCommand Tests

    [Fact]
    public void ParticipantListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantListCommand_ShouldHaveRequiredOrgIdOption()
    {
        var command = new ParticipantListCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion

    #region ParticipantGetCommand Tests

    [Fact]
    public void ParticipantGetCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantGetCommand_ShouldHaveRequiredOrgIdOption()
    {
        var command = new ParticipantGetCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantGetCommand_ShouldHaveRequiredIdOption()
    {
        var command = new ParticipantGetCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion

    #region ParticipantUpdateCommand Tests

    [Fact]
    public void ParticipantUpdateCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantUpdateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("update");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantUpdateCommand_ShouldHaveRequiredOrgIdOption()
    {
        var command = new ParticipantUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantUpdateCommand_ShouldHaveRequiredIdOption()
    {
        var command = new ParticipantUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantUpdateCommand_ShouldHaveOptionalDisplayNameOption()
    {
        var command = new ParticipantUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--display-name");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    [Fact]
    public void ParticipantUpdateCommand_ShouldHaveOptionalStatusOption()
    {
        var command = new ParticipantUpdateCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--status");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion

    #region ParticipantSearchCommand Tests

    [Fact]
    public void ParticipantSearchCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantSearchCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("search");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantSearchCommand_ShouldHaveRequiredQueryOption()
    {
        var command = new ParticipantSearchCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--query");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantSearchCommand_ShouldHaveOptionalStatusOption()
    {
        var command = new ParticipantSearchCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--status");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion

    #region ParticipantWalletLinkCommand Tests

    [Fact]
    public void ParticipantWalletLinkCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ParticipantWalletLinkCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("wallet-link");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantWalletLinkCommand_ShouldHaveRequiredParticipantIdOption()
    {
        var command = new ParticipantWalletLinkCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--participant-id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void ParticipantWalletLinkCommand_ShouldHaveRequiredWalletAddressOption()
    {
        var command = new ParticipantWalletLinkCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--wallet-address");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    #endregion
}
