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
/// Unit tests for Peer command structure and options.
/// </summary>
public class PeerCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public PeerCommandsTests()
    {
        _mockAuthService = new Mock<IAuthenticationService>();
        _mockConfigService = new Mock<IConfigurationService>();

        // Setup default mock behavior
        _mockConfigService.Setup(x => x.GetActiveProfileAsync())
            .ReturnsAsync(new Profile { Name = "test" });
        _mockConfigService.Setup(x => x.GetProfileAsync(It.IsAny<string>()))
            .ReturnsAsync(new Profile { Name = "test", ServiceUrl = "http://localhost:80" });
        _mockAuthService.Setup(x => x.GetAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync("test-token");

        // HttpClientFactory requires a real IConfigurationService, so we use the mock
        _clientFactory = new HttpClientFactory(_mockConfigService.Object);
    }

    private IAuthenticationService AuthService => _mockAuthService.Object;
    private IConfigurationService ConfigService => _mockConfigService.Object;

    [Fact]
    public void PeerCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new PeerCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("peer");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeerCommand_ShouldHaveExpectedSubcommands()
    {
        var command = new PeerCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(10);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "list", "get", "stats", "health", "quality", "subscriptions", "subscribe", "unsubscribe", "ban", "reset" });
    }

    #region PeerListCommand Tests

    [Fact]
    public void PeerListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new PeerListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeerListCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new PeerListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region PeerGetCommand Tests

    [Fact]
    public void PeerGetCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new PeerGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeerGetCommand_ShouldHaveRequiredIdOption()
    {
        var command = new PeerGetCommand(_clientFactory, AuthService, ConfigService);
        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void PeerGetCommand_ShouldParseArguments_WithRequiredId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new PeerGetCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("get --id peer-123");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region PeerStatsCommand Tests

    [Fact]
    public void PeerStatsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new PeerStatsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("stats");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeerStatsCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new PeerStatsCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("stats");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion

    #region PeerHealthCommand Tests

    [Fact]
    public void PeerHealthCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new PeerHealthCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("health");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void PeerHealthCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new PeerHealthCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("health");
        parseResult.Errors.Should().BeEmpty();
    }

    #endregion
}
