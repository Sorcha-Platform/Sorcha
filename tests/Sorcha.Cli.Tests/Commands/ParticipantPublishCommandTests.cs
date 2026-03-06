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
/// Unit tests for ParticipantPublishCommand and ParticipantUnpublishCommand structure and options.
/// </summary>
public class ParticipantPublishCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ParticipantPublishCommandTests()
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

    #region ParticipantPublishCommand Tests

    [Fact]
    public void ParticipantPublishCommand_ShouldHaveCorrectName()
    {
        var command = new ParticipantPublishCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("publish");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantPublishCommand_ShouldHaveRequiredOptions()
    {
        var command = new ParticipantPublishCommand(_clientFactory, AuthService, ConfigService);

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();

        var nameOption = command.Options.FirstOrDefault(o => o.Name == "--name");
        nameOption.Should().NotBeNull();
        nameOption!.Required.Should().BeTrue();

        var orgNameOption = command.Options.FirstOrDefault(o => o.Name == "--org-name");
        orgNameOption.Should().NotBeNull();
        orgNameOption!.Required.Should().BeTrue();

        var walletOption = command.Options.FirstOrDefault(o => o.Name == "--wallet");
        walletOption.Should().NotBeNull();
        walletOption!.Required.Should().BeTrue();

        var signerOption = command.Options.FirstOrDefault(o => o.Name == "--signer");
        signerOption.Should().NotBeNull();
        signerOption!.Required.Should().BeTrue();
    }

    #endregion

    #region ParticipantUnpublishCommand Tests

    [Fact]
    public void ParticipantUnpublishCommand_ShouldHaveCorrectName()
    {
        var command = new ParticipantUnpublishCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("unpublish");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ParticipantUnpublishCommand_ShouldHaveRequiredOptions()
    {
        var command = new ParticipantUnpublishCommand(_clientFactory, AuthService, ConfigService);

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();

        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();

        var signerOption = command.Options.FirstOrDefault(o => o.Name == "--signer");
        signerOption.Should().NotBeNull();
        signerOption!.Required.Should().BeTrue();
    }

    #endregion
}
