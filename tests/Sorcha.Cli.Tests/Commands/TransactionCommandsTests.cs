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
/// Unit tests for Transaction command structure and options.
/// </summary>
public class TransactionCommandsTests
{
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;
    private readonly HttpClientFactory _clientFactory;

    public TransactionCommandsTests()
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
    public void TransactionCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TransactionCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("tx");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TransactionCommand_ShouldHaveAllSubcommands()
    {
        var command = new TransactionCommand(_clientFactory, AuthService, ConfigService);
        command.Subcommands.Should().HaveCount(7);
        command.Subcommands.Select(c => c.Name).Should().Contain(
            new[] { "list", "get", "submit", "status", "proof", "verify-proof", "revoke" });
    }

    #region TxListCommand Tests

    [Fact]
    public void TxListCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxListCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("list");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxListCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new TxListCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxListCommand_ShouldHaveOptionalPageOption()
    {
        var command = new TxListCommand(_clientFactory, AuthService, ConfigService);
        var pageOption = command.Options.FirstOrDefault(o => o.Name == "--page");
        pageOption.Should().NotBeNull();
        pageOption!.Required.Should().BeFalse();
    }

    [Fact]
    public void TxListCommand_ShouldHaveOptionalPageSizeOption()
    {
        var command = new TxListCommand(_clientFactory, AuthService, ConfigService);
        var pageSizeOption = command.Options.FirstOrDefault(o => o.Name == "--page-size");
        pageSizeOption.Should().NotBeNull();
        pageSizeOption!.Required.Should().BeFalse();
    }

    #endregion

    #region TxGetCommand Tests

    [Fact]
    public void TxGetCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxGetCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("get");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxGetCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new TxGetCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxGetCommand_ShouldHaveRequiredTxIdOption()
    {
        var command = new TxGetCommand(_clientFactory, AuthService, ConfigService);
        var txIdOption = command.Options.FirstOrDefault(o => o.Name == "--tx-id");
        txIdOption.Should().NotBeNull();
        txIdOption!.Required.Should().BeTrue();
    }

    #endregion

    #region TxSubmitCommand Tests

    [Fact]
    public void TxSubmitCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("submit");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxSubmitCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxSubmitCommand_ShouldHaveRequiredTypeOption()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        var typeOption = command.Options.FirstOrDefault(o => o.Name == "--type");
        typeOption.Should().NotBeNull();
        typeOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxSubmitCommand_ShouldHaveRequiredWalletOption()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        var walletOption = command.Options.FirstOrDefault(o => o.Name == "--wallet");
        walletOption.Should().NotBeNull();
        walletOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxSubmitCommand_ShouldHaveRequiredPayloadOption()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        var payloadOption = command.Options.FirstOrDefault(o => o.Name == "--payload");
        payloadOption.Should().NotBeNull();
        payloadOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxSubmitCommand_ShouldHaveRequiredSignatureOption()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        var signatureOption = command.Options.FirstOrDefault(o => o.Name == "--signature");
        signatureOption.Should().NotBeNull();
        signatureOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxSubmitCommand_ShouldHaveOptionalPreviousTxOption()
    {
        var command = new TxSubmitCommand(_clientFactory, AuthService, ConfigService);
        var previousTxOption = command.Options.FirstOrDefault(o => o.Name == "--previous-tx");
        previousTxOption.Should().NotBeNull();
        previousTxOption!.Required.Should().BeFalse();
    }

    #endregion

    #region TxStatusCommand Tests

    [Fact]
    public void TxStatusCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxStatusCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("status");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxStatusCommand_ShouldHaveRequiredRegisterIdOption()
    {
        var command = new TxStatusCommand(_clientFactory, AuthService, ConfigService);
        var registerIdOption = command.Options.FirstOrDefault(o => o.Name == "--register-id");
        registerIdOption.Should().NotBeNull();
        registerIdOption!.Required.Should().BeTrue();
    }

    [Fact]
    public void TxStatusCommand_ShouldHaveRequiredTxIdOption()
    {
        var command = new TxStatusCommand(_clientFactory, AuthService, ConfigService);
        var txIdOption = command.Options.FirstOrDefault(o => o.Name == "--tx-id");
        txIdOption.Should().NotBeNull();
        txIdOption!.Required.Should().BeTrue();
    }

    #endregion

    #region TxProofCommand Tests

    [Fact]
    public void TxProofCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxProofCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("proof");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxProofCommand_ShouldHaveRequiredRegisterAndTxOptions()
    {
        var command = new TxProofCommand(_clientFactory, AuthService, ConfigService);
        command.Options.First(o => o.Name == "--register-id").Required.Should().BeTrue();
        command.Options.First(o => o.Name == "--tx-id").Required.Should().BeTrue();
    }

    [Fact]
    public void TxProofCommand_ShouldHaveOptionalOutOption()
    {
        var command = new TxProofCommand(_clientFactory, AuthService, ConfigService);
        var outOption = command.Options.FirstOrDefault(o => o.Name == "--out");
        outOption.Should().NotBeNull();
        outOption!.Required.Should().BeFalse();
    }

    #endregion

    #region TxVerifyProofCommand Tests

    [Fact]
    public void TxVerifyProofCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxVerifyProofCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("verify-proof");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxVerifyProofCommand_ShouldHaveRequiredFileOption()
    {
        var command = new TxVerifyProofCommand(_clientFactory, AuthService, ConfigService);
        var fileOption = command.Options.FirstOrDefault(o => o.Name == "--file");
        fileOption.Should().NotBeNull();
        fileOption!.Required.Should().BeTrue();
    }

    #endregion

    #region TxRevokeCommand Tests

    [Fact]
    public void TxRevokeCommand_ShouldHaveCorrectNameAndDescription()
    {
        var command = new TxRevokeCommand(_clientFactory, AuthService, ConfigService);
        command.Name.Should().Be("revoke");
        command.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TxRevokeCommand_ShouldHaveRequiredTxIdAndReasonOptions()
    {
        var command = new TxRevokeCommand(_clientFactory, AuthService, ConfigService);
        command.Options.First(o => o.Name == "--tx-id").Required.Should().BeTrue();
        command.Options.First(o => o.Name == "--reason").Required.Should().BeTrue();
    }

    [Fact]
    public void TxRevokeCommand_ShouldHaveOptionalSupersededByOption()
    {
        var command = new TxRevokeCommand(_clientFactory, AuthService, ConfigService);
        var option = command.Options.FirstOrDefault(o => o.Name == "--superseded-by");
        option.Should().NotBeNull();
        option!.Required.Should().BeFalse();
    }

    #endregion
}
