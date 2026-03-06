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
/// Unit tests for credential lifecycle CLI commands (suspend, reinstate, refresh).
/// </summary>
public class CredentialLifecycleCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public CredentialLifecycleCommandTests()
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

    #region CredentialSuspendCommand Tests

    [Fact]
    public void CredentialSuspendCommand_ShouldHaveCorrectName()
    {
        var command = new CredentialSuspendCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("suspend");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialSuspendCommand_ShouldHaveRequiredOptions()
    {
        var command = new CredentialSuspendCommand(_clientFactory, AuthService, ConfigService);

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();

        var walletOption = command.Options.FirstOrDefault(o => o.Name == "--wallet");
        walletOption.Should().NotBeNull();
        walletOption!.Required.Should().BeTrue();

        var reasonOption = command.Options.FirstOrDefault(o => o.Name == "--reason");
        reasonOption.Should().NotBeNull();
        reasonOption!.Required.Should().BeFalse();
    }

    #endregion

    #region CredentialReinstateCommand Tests

    [Fact]
    public void CredentialReinstateCommand_ShouldHaveCorrectName()
    {
        var command = new CredentialReinstateCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("reinstate");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialReinstateCommand_ShouldHaveRequiredOptions()
    {
        var command = new CredentialReinstateCommand(_clientFactory, AuthService, ConfigService);

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();

        var walletOption = command.Options.FirstOrDefault(o => o.Name == "--wallet");
        walletOption.Should().NotBeNull();
        walletOption!.Required.Should().BeTrue();

        var reasonOption = command.Options.FirstOrDefault(o => o.Name == "--reason");
        reasonOption.Should().NotBeNull();
        reasonOption!.Required.Should().BeFalse();
    }

    #endregion

    #region CredentialRefreshCommand Tests

    [Fact]
    public void CredentialRefreshCommand_ShouldHaveCorrectName()
    {
        var command = new CredentialRefreshCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("refresh");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CredentialRefreshCommand_ShouldHaveRequiredOptions()
    {
        var command = new CredentialRefreshCommand(_clientFactory, AuthService, ConfigService);

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();

        var walletOption = command.Options.FirstOrDefault(o => o.Name == "--wallet");
        walletOption.Should().NotBeNull();
        walletOption!.Required.Should().BeTrue();

        var expiresOption = command.Options.FirstOrDefault(o => o.Name == "--expires-in-days");
        expiresOption.Should().NotBeNull();
        expiresOption!.Required.Should().BeFalse();
    }

    #endregion
}
