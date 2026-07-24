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
/// Unit tests for the instance-repair command structure and options (Feature 145 US4).
/// </summary>
public class InstanceCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService = new();
    private readonly Mock<IConfigurationService> _mockConfigService = new();
    private readonly HttpClientFactory _clientFactory;

    public InstanceCommandsTests()
    {
        _mockConfigService.Setup(x => x.GetActiveProfileAsync())
            .ReturnsAsync(new Profile { Name = "test" });
        _mockAuthService.Setup(x => x.GetAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync("test-token");

        _clientFactory = new HttpClientFactory(_mockConfigService.Object);
    }

    private IAuthenticationService AuthService => _mockAuthService.Object;
    private IConfigurationService ConfigService => _mockConfigService.Object;

    [Fact]
    public void InstanceCommand_HasParityAndRebuildSubcommands()
    {
        var command = new InstanceCommand(_clientFactory, AuthService, ConfigService);

        command.Name.Should().Be("instance");
        command.Subcommands.Should().Contain(c => c.Name == "parity");
        command.Subcommands.Should().Contain(c => c.Name == "rebuild");
    }

    [Fact]
    public void InstanceParityCommand_RequiresRegisterAndInstanceIds()
    {
        var command = new InstanceParityCommand(_clientFactory, AuthService, ConfigService);

        var registerId = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerId.Should().NotBeNull();
        registerId!.Required.Should().BeTrue();

        var instanceId = command.Options.FirstOrDefault(o => o.Name == "--instance-id");
        instanceId.Should().NotBeNull();
        instanceId!.Required.Should().BeTrue();
    }

    [Fact]
    public void InstanceRebuildCommand_RequiresIdsAndHasConfirmFlag()
    {
        var command = new InstanceRebuildCommand(_clientFactory, AuthService, ConfigService);

        command.Options.FirstOrDefault(o => o.Name == "--register-id")!.Required.Should().BeTrue();
        command.Options.FirstOrDefault(o => o.Name == "--instance-id")!.Required.Should().BeTrue();

        // Rebuild overwrites state, so it must offer an explicit confirmation-skip flag rather than
        // running destructively by default.
        var confirm = command.Options.FirstOrDefault(o => o.Name == "--yes");
        confirm.Should().NotBeNull();
        confirm!.Required.Should().BeFalse();
    }
}
