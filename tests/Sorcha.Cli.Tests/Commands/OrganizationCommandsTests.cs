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
/// Unit tests for organization commands.
/// </summary>
public class OrganizationCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public OrganizationCommandsTests()
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

        _clientFactory = new HttpClientFactory(_mockConfigService.Object);
    }

    private IAuthenticationService AuthService => _mockAuthService.Object;
    private IConfigurationService ConfigService => _mockConfigService.Object;

    [Fact]
    public void OrganizationCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new OrganizationCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("org");
        command.Description.Should().Be("Manage organizations");
    }

    [Fact]
    public void OrganizationCommand_ShouldHaveAllSubcommands()
    {
        // Arrange & Act
        var command = new OrganizationCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Subcommands.Should().HaveCount(5);
        command.Subcommands.Should().Contain(c => c.Name == "list");
        command.Subcommands.Should().Contain(c => c.Name == "get");
        command.Subcommands.Should().Contain(c => c.Name == "create");
        command.Subcommands.Should().Contain(c => c.Name == "update");
        command.Subcommands.Should().Contain(c => c.Name == "delete");
    }

    [Fact]
    public void OrgListCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new OrgListCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("list");
        command.Description.Should().Be("List all organizations");
    }

    [Fact]
    public void OrgGetCommand_ShouldHaveRequiredIdOption()
    {
        // Arrange & Act
        var command = new OrgGetCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("get");
        command.Description.Should().Be("Get an organization by ID");

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void OrgCreateCommand_ShouldHaveRequiredNameOption()
    {
        // Arrange & Act
        var command = new OrgCreateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("create");
        command.Description.Should().Be("Create a new organization");

        var nameOption = command.Options.FirstOrDefault(o => o.Name == "--name");
        nameOption.Should().NotBeNull();
        nameOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void OrgCreateCommand_ShouldHaveRequiredSubdomainOption()
    {
        // Arrange & Act
        var command = new OrgCreateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        var subdomainOption = command.Options.FirstOrDefault(o => o.Name == "--subdomain");
        subdomainOption.Should().NotBeNull();
        subdomainOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void OrgUpdateCommand_ShouldHaveRequiredIdOption()
    {
        // Arrange & Act
        var command = new OrgUpdateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("update");
        command.Description.Should().Be("Update an organization");

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void OrgUpdateCommand_ShouldHaveOptionalNameAndStatusOptions()
    {
        // Arrange & Act
        var command = new OrgUpdateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        var nameOption = command.Options.FirstOrDefault(o => o.Name == "--name");
        nameOption.Should().NotBeNull();
        nameOption!.Required.Should().BeFalse();

        var statusOption = command.Options.FirstOrDefault(o => o.Name == "--status");
        statusOption.Should().NotBeNull();
        statusOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void OrgDeleteCommand_ShouldHaveRequiredIdOption()
    {
        // Arrange & Act
        var command = new OrgDeleteCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("delete");
        command.Description.Should().Be("Delete an organization");

        var idOption = command.Options.FirstOrDefault(o => o.Name == "--id");
        idOption.Should().NotBeNull();
        idOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void OrgDeleteCommand_ShouldHaveOptionalYesOption()
    {
        // Arrange & Act
        var command = new OrgDeleteCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void OrgListCommand_ShouldParseArguments()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new OrgListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void OrgGetCommand_ShouldParseArguments_WithRequiredId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new OrgGetCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("get --id test-org-123");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void OrgCreateCommand_ShouldParseArguments_WithRequiredFields()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new OrgCreateCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("create --name \"Test Org\" --subdomain testorg");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void OrgCreateCommand_ShouldParseArguments_WithAllOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new OrgCreateCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("create --name \"Test Org\" --subdomain testorg");
        parseResult.Errors.Should().BeEmpty();
    }
}
