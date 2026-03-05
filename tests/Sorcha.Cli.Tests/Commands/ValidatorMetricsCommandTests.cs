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
/// Unit tests for Validator metrics command structure and options.
/// </summary>
public class ValidatorMetricsCommandTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public ValidatorMetricsCommandTests()
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

    #region ValidatorMetricsCommand Tests

    [Fact]
    public void ValidatorMetricsCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("metrics");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidatorMetricsCommand_ShouldHaveFiveSubcommands()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(5);
        command.Subcommands.Select(c => c.Name).Should().Contain(new[] { "validation", "consensus", "pools", "caches", "config" });
    }

    #endregion

    #region Validation Subcommand Tests

    [Fact]
    public void ValidatorMetricsValidationSubcommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        var subcommand = command.Subcommands.FirstOrDefault(c => c.Name == "validation");
        subcommand.Should().NotBeNull();
        subcommand!.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Consensus Subcommand Tests

    [Fact]
    public void ValidatorMetricsConsensusSubcommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        var subcommand = command.Subcommands.FirstOrDefault(c => c.Name == "consensus");
        subcommand.Should().NotBeNull();
        subcommand!.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Pools Subcommand Tests

    [Fact]
    public void ValidatorMetricsPoolsSubcommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        var subcommand = command.Subcommands.FirstOrDefault(c => c.Name == "pools");
        subcommand.Should().NotBeNull();
        subcommand!.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Caches Subcommand Tests

    [Fact]
    public void ValidatorMetricsCachesSubcommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        var subcommand = command.Subcommands.FirstOrDefault(c => c.Name == "caches");
        subcommand.Should().NotBeNull();
        subcommand!.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Config Subcommand Tests

    [Fact]
    public void ValidatorMetricsConfigSubcommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        var subcommand = command.Subcommands.FirstOrDefault(c => c.Name == "config");
        subcommand.Should().NotBeNull();
        subcommand!.Description.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Subcommand No-Options Tests

    [Theory]
    [InlineData("validation")]
    [InlineData("consensus")]
    [InlineData("pools")]
    [InlineData("caches")]
    [InlineData("config")]
    public void ValidatorMetricsSubcommands_ShouldHaveNoOptions(string subcommandName)
    {
        var command = new ValidatorMetricsCommand(_clientFactory, AuthService, ConfigService);
        var subcommand = command.Subcommands.FirstOrDefault(c => c.Name == subcommandName);
        subcommand.Should().NotBeNull();
        subcommand!.Options.Should().BeEmpty();
    }

    #endregion
}
