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
/// Unit tests for user commands.
/// </summary>
public class UserCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public UserCommandsTests()
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
    public void UserCommand_ShouldHaveCorrectNameAndDescription()
    {
        // Arrange & Act
        var command = new UserCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("user");
        command.Description.Should().Be("Manage users within organizations");
    }

    [Fact]
    public void UserCommand_ShouldHaveAllSubcommands()
    {
        // Arrange & Act
        var command = new UserCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Subcommands.Should().HaveCount(5);
        command.Subcommands.Should().Contain(c => c.Name == "list");
        command.Subcommands.Should().Contain(c => c.Name == "get");
        command.Subcommands.Should().Contain(c => c.Name == "create");
        command.Subcommands.Should().Contain(c => c.Name == "update");
        command.Subcommands.Should().Contain(c => c.Name == "delete");
    }

    [Fact]
    public void UserListCommand_ShouldHaveRequiredOrgIdOption()
    {
        // Arrange & Act
        var command = new UserListCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("list");
        command.Description.Should().Be("List all users in an organization");

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void UserGetCommand_ShouldHaveRequiredOrgIdAndUserIdOptions()
    {
        // Arrange & Act
        var command = new UserGetCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("get");
        command.Description.Should().Be("Get a user by ID");

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var userIdOption = command.Options.FirstOrDefault(o => o.Name == "--user-id");
        userIdOption.Should().NotBeNull();
        userIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void UserCreateCommand_ShouldHaveRequiredOptions()
    {
        // Arrange & Act
        var command = new UserCreateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("create");
        command.Description.Should().Be("Create a new user in an organization");

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var usernameOption = command.Options.FirstOrDefault(o => o.Name == "--username");
        usernameOption.Should().NotBeNull();
        usernameOption!.Required.Should().BeTrue();

        var emailOption = command.Options.FirstOrDefault(o => o.Name == "--email");
        emailOption.Should().NotBeNull();
        emailOption!.Required.Should().BeTrue();

        var passwordOption = command.Options.FirstOrDefault(o => o.Name == "--password");
        passwordOption.Should().NotBeNull();
        passwordOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void UserCreateCommand_ShouldHaveOptionalNameAndRolesOptions()
    {
        // Arrange & Act
        var command = new UserCreateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        var firstNameOption = command.Options.FirstOrDefault(o => o.Name == "--first-name");
        firstNameOption.Should().NotBeNull();
        firstNameOption!.Required.Should().BeFalse();

        var lastNameOption = command.Options.FirstOrDefault(o => o.Name == "--last-name");
        lastNameOption.Should().NotBeNull();
        lastNameOption!.Required.Should().BeFalse();

        var rolesOption = command.Options.FirstOrDefault(o => o.Name == "--roles");
        rolesOption.Should().NotBeNull();
        rolesOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void UserUpdateCommand_ShouldHaveRequiredOrgIdAndUserIdOptions()
    {
        // Arrange & Act
        var command = new UserUpdateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("update");
        command.Description.Should().Be("Update a user");

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var userIdOption = command.Options.FirstOrDefault(o => o.Name == "--user-id");
        userIdOption.Should().NotBeNull();
        userIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void UserUpdateCommand_ShouldHaveOptionalUpdateOptions()
    {
        // Arrange & Act
        var command = new UserUpdateCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        var emailOption = command.Options.FirstOrDefault(o => o.Name == "--email");
        emailOption.Should().NotBeNull();
        emailOption!.Required.Should().BeFalse();

        var firstNameOption = command.Options.FirstOrDefault(o => o.Name == "--first-name");
        firstNameOption.Should().NotBeNull();
        firstNameOption!.Required.Should().BeFalse();

        var lastNameOption = command.Options.FirstOrDefault(o => o.Name == "--last-name");
        lastNameOption.Should().NotBeNull();
        lastNameOption!.Required.Should().BeFalse();

        var activeOption = command.Options.FirstOrDefault(o => o.Name == "--active");
        activeOption.Should().NotBeNull();
        activeOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void UserDeleteCommand_ShouldHaveRequiredOrgIdAndUserIdOptions()
    {
        // Arrange & Act
        var command = new UserDeleteCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        command.Name.Should().Be("delete");
        command.Description.Should().Be("Delete a user");

        var orgIdOption = command.Options.FirstOrDefault(o => o.Name == "--org-id");
        orgIdOption.Should().NotBeNull();
        orgIdOption!.Required.Should().BeTrue();

        var userIdOption = command.Options.FirstOrDefault(o => o.Name == "--user-id");
        userIdOption.Should().NotBeNull();
        userIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void UserDeleteCommand_ShouldHaveOptionalYesOption()
    {
        // Arrange & Act
        var command = new UserDeleteCommand(_clientFactory, AuthService, ConfigService);

        // Assert
        var yesOption = command.Options.FirstOrDefault(o => o.Name == "--yes");
        yesOption.Should().NotBeNull();
        yesOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void UserListCommand_ShouldParseArguments_WithRequiredOrgId()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new UserListCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("list --org-id test-org-123");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UserGetCommand_ShouldParseArguments_WithRequiredOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new UserGetCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("get --org-id test-org-123 --user-id user-456");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UserCreateCommand_ShouldParseArguments_WithRequiredOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new UserCreateCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("create --org-id test-org-123 --username john --email john@test.com --password test123");
        parseResult.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UserCreateCommand_ShouldParseArguments_WithAllOptions()
    {
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(new UserCreateCommand(_clientFactory, AuthService, ConfigService));
        var parseResult = rootCommand.Parse("create --org-id test-org-123 --username john --email john@test.com --password test123 --first-name John --last-name Doe --roles Admin,User");
        parseResult.Errors.Should().BeEmpty();
    }
}
