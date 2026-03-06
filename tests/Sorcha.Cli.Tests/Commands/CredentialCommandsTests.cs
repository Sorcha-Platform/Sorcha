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
/// Unit tests for Credential command structure and options.
/// </summary>
public class CredentialCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public CredentialCommandsTests()
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
    public void CredentialCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("credential");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialCommand_ShouldHaveElevenSubcommands()
    {
        var command = new CredentialCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(11);
        command.Subcommands.Select(c => c.Name).Should().Contain(
            new[] { "list", "get", "issue", "present", "verify", "revoke", "status", "suspend", "reinstate", "refresh", "status-list" });
    }

    #region CredentialListCommand Tests

    [Fact]
    public void CredentialListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region CredentialGetCommand Tests

    [Fact]
    public void CredentialGetCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialGetCommand_ShouldHaveRequiredIdOption()
    {
        var command = new CredentialGetCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion

    #region CredentialIssueCommand Tests

    [Fact]
    public void CredentialIssueCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialIssueCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("issue");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialIssueCommand_ShouldHaveRequiredTypeOption()
    {
        var command = new CredentialIssueCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--type");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialIssueCommand_ShouldHaveRequiredSubjectOption()
    {
        var command = new CredentialIssueCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--subject");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialIssueCommand_ShouldHaveRequiredWalletOption()
    {
        var command = new CredentialIssueCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--wallet");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialIssueCommand_ShouldHaveRequiredClaimsOption()
    {
        var command = new CredentialIssueCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--claims");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialIssueCommand_ShouldHaveOptionalExpiresInDaysOption()
    {
        var command = new CredentialIssueCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--expires-in-days");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion

    #region CredentialPresentCommand Tests

    [Fact]
    public void CredentialPresentCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialPresentCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("present");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialPresentCommand_ShouldHaveRequiredIdOption()
    {
        var command = new CredentialPresentCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--id");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialPresentCommand_ShouldHaveRequiredVerifierOption()
    {
        var command = new CredentialPresentCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--verifier");
        option.Should().NotBeNull();
        option!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialPresentCommand_ShouldHaveOptionalClaimsOption()
    {
        var command = new CredentialPresentCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--claims");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion

    #region CredentialVerifyCommand Tests

    [Fact]
    public void CredentialVerifyCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialVerifyCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("verify");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialVerifyCommand_ShouldHaveRequiredIdOption()
    {
        var command = new CredentialVerifyCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion

    #region CredentialRevokeCommand Tests

    [Fact]
    public void CredentialRevokeCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialRevokeCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("revoke");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialRevokeCommand_ShouldHaveRequiredIdOption()
    {
        var command = new CredentialRevokeCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void CredentialRevokeCommand_ShouldHaveOptionalYesOption()
    {
        var command = new CredentialRevokeCommand(_clientFactory, AuthService, ConfigService);
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    #endregion

    #region CredentialStatusCommand Tests

    [Fact]
    public void CredentialStatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new CredentialStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("status");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialStatusCommand_ShouldHaveRequiredIdOption()
    {
        var command = new CredentialStatusCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    #endregion
}
