// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using FluentAssertions;
using Moq;
using Sorcha.Cli.Commands;
using Sorcha.Cli.Services;
using Sorcha.Cli.Models;
using Xunit;

namespace Sorcha.Cli.Tests.Commands;

/// <summary>
/// Unit tests for the trusted-list admin command structure and options (Feature 181 US3).
/// </summary>
public class TrustCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService = new();
    private readonly Mock<IConfigurationService> _mockConfigService = new();
    private readonly HttpClientFactory _clientFactory;

    public TrustCommandsTests()
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
    public void TrustCommand_HasAllSubcommands()
    {
        var command = new TrustCommand(_clientFactory, AuthService, ConfigService);

        command.Name.Should().Be("trust");
        command.Subcommands.Should().Contain(c => c.Name == "list");
        command.Subcommands.Should().Contain(c => c.Name == "get");
        command.Subcommands.Should().Contain(c => c.Name == "import");
        command.Subcommands.Should().Contain(c => c.Name == "delete");
    }

    [Fact]
    public void TrustGetCommand_RequiresId()
    {
        var command = new TrustGetCommand(_clientFactory, AuthService, ConfigService);

        var id = command.Options.FirstOrDefault(o => o.Name == "--id");
        id.Should().NotBeNull();
        id!.Required.Should().BeTrue();
    }

    [Fact]
    public void TrustImportCommand_HasIdFileAndUrlOptions()
    {
        var command = new TrustImportCommand(_clientFactory, AuthService, ConfigService);

        command.Options.FirstOrDefault(o => o.Name == "--id")!.Required.Should().BeTrue();

        // File and URL are the two mutually-exclusive import sources — both optional at parse time,
        // the exactly-one rule is enforced in the action.
        var file = command.Options.FirstOrDefault(o => o.Name == "--file");
        file.Should().NotBeNull();
        file!.Required.Should().BeFalse();

        var url = command.Options.FirstOrDefault(o => o.Name == "--url");
        url.Should().NotBeNull();
        url!.Required.Should().BeFalse();
    }

    [Fact]
    public void TrustDeleteCommand_RequiresIdAndHasConfirmFlag()
    {
        var command = new TrustDeleteCommand(_clientFactory, AuthService, ConfigService);

        command.Options.FirstOrDefault(o => o.Name == "--id")!.Required.Should().BeTrue();

        // Delete removes anchors verifying services depend on, so it must offer an explicit
        // confirmation-skip flag rather than running destructively by default.
        var confirm = command.Options.FirstOrDefault(o => o.Name == "--yes");
        confirm.Should().NotBeNull();
        confirm!.Required.Should().BeFalse();
    }
}
