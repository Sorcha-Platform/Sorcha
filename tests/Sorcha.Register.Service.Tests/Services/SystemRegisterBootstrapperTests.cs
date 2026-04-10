// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceDefaults;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class SystemRegisterBootstrapperTests
{
    private readonly Mock<ILogger<SystemRegisterBootstrapper>> _bootstrapperLogger = new();
    private readonly Mock<ILogger<GenesisIngestionService>> _ingestionLogger = new();
    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<ICryptoModule> _cryptoModule = new();

    [Fact]
    public void Constructor_ThrowsOnNullScopeFactory()
    {
        var act = () => new SystemRegisterBootstrapper(null!, _bootstrapperLogger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var act = () => new SystemRegisterBootstrapper(scopeFactory.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GenesisIngestionService_LoadAndVerify_ReturnsNullWhenNoGenesis()
    {
        // No genesis file configured, placeholder embedded
        var options = Options.Create(new SystemRegisterOptions { GenesisFile = null });
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var result = await service.LoadAndVerifyGenesisAsync(CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public void GenesisIngestionService_LoadAndVerify_ThrowsOnMissingConfiguredFile()
    {
        var options = Options.Create(new SystemRegisterOptions { GenesisFile = "/nonexistent/genesis.json" });
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var act = () => service.LoadAndVerifyGenesisAsync(CancellationToken.None);
        act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task GenesisIngestionService_IngestGenesis_SubmitsToValidator()
    {
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        _validatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "test-tx",
                RegisterId = SystemRegisterConstants.SystemRegisterId
            });

        var genesis = CreateTestGenesis();
        var result = await service.IngestGenesisAsync(genesis, CancellationToken.None);

        result.Should().BeTrue();
        _validatorClient.Verify(
            v => v.SubmitTransactionAsync(
                It.Is<TransactionSubmission>(s =>
                    s.RegisterId == SystemRegisterConstants.SystemRegisterId &&
                    s.BlueprintId == GenesisConstants.BlueprintId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenesisIngestionService_IngestGenesis_ReturnsFalseOnRejection()
    {
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        _validatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorCode = "REJECTED",
                ErrorMessage = "Not in validator roster"
            });

        var genesis = CreateTestGenesis();
        var result = await service.IngestGenesisAsync(genesis, CancellationToken.None);

        result.Should().BeFalse();
    }

    private static SystemRegisterGenesis CreateTestGenesis()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x01);
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"registerId\":\"test\"}");

        return new SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test-network",
            GenesisTransaction = new GenesisTransactionData
            {
                TxId = GenesisSignatureVerifier.ComputeGenesisTxId(),
                Payload = Convert.ToBase64String(payload),
                PayloadHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
                Signature = new GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = new Register.Models.ValidatorRoster
            {
                Validators =
                [
                    new Register.Models.ValidatorRosterEntry
                    {
                        ValidatorId = "test-validator",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = Register.Models.SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = Register.Models.ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = GenesisFileLoader.ComputeFingerprint(publicKey)
        };
    }
}
