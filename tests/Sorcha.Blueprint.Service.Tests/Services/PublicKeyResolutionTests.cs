// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;
using Sorcha.ServiceClients.Validator;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Cryptography.Enums;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for US4: Public key resolution from register with external key override.
/// Validates T036: register-published keys, external precedence, mixed sources,
/// revoked participant error, and not-found warning/skip behavior.
/// </summary>
public class PublicKeyResolutionTests
{
    private readonly Mock<IActionResolverService> _mockActionResolver;
    private readonly Mock<IStateReconstructionService> _mockStateReconstruction;
    private readonly Mock<ITransactionBuilderService> _mockTransactionBuilder;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly Mock<IValidatorServiceClient> _mockValidatorClient;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IParticipantServiceClient> _mockParticipantClient;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IInstanceStore> _mockInstanceStore;
    private readonly Mock<IActionStore> _mockActionStore;
    private readonly Mock<IExecutionEngine> _mockExecutionEngine;
    private readonly Mock<ILogger<ActionExecutionService>> _mockLogger;
    private readonly Mock<IEncryptionPipelineService> _mockEncryptionPipeline;

    public PublicKeyResolutionTests()
    {
        _mockActionResolver = new Mock<IActionResolverService>();
        _mockStateReconstruction = new Mock<IStateReconstructionService>();
        _mockTransactionBuilder = new Mock<ITransactionBuilderService>();
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
        _mockValidatorClient = new Mock<IValidatorServiceClient>();
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockParticipantClient = new Mock<IParticipantServiceClient>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockInstanceStore = new Mock<IInstanceStore>();
        _mockActionStore = new Mock<IActionStore>();
        _mockExecutionEngine = new Mock<IExecutionEngine>();
        _mockLogger = new Mock<ILogger<ActionExecutionService>>();
        _mockEncryptionPipeline = new Mock<IEncryptionPipelineService>();

        // Default: no idempotency collision
        _mockActionStore.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string?)null);
    }

    private ActionExecutionService CreateService()
    {
        return new ActionExecutionService(
            _mockActionResolver.Object,
            _mockStateReconstruction.Object,
            _mockTransactionBuilder.Object,
            _mockRegisterClient.Object,
            _mockValidatorClient.Object,
            _mockWalletClient.Object,
            _mockParticipantClient.Object,
            _mockNotificationService.Object,
            _mockInstanceStore.Object,
            _mockActionStore.Object,
            _mockExecutionEngine.Object,
            _mockLogger.Object,
            new ConfigurationBuilder().Build(),
            credentialVerifier: null,
            confirmationOptions: null,
            statusListManager: null,
            encryptionPipeline: _mockEncryptionPipeline.Object);
    }

    #region T036: Public Key Resolution Tests

    [Fact]
    public async Task ResolveRecipientKeys_RegisterPublishedKeys_ResolvedWithRegisterSource()
    {
        // Arrange
        var service = CreateService();
        var instanceId = "test-instance";
        var actionId = 1;
        var registerId = "register-1";
        var walletAddress = "wallet-recipient";
        var publicKeyBytes = new byte[32];
        new Random(42).NextBytes(publicKeyBytes);
        var publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);

        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = walletAddress,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1"
            }
            // No ExternalRecipientKeys — resolution must come from register
        };

        var instance = CreateTestInstance(instanceId, "blueprint-1", registerId,
            new Dictionary<string, string> { ["applicant"] = walletAddress });
        var blueprint = CreateTestBlueprint(walletAddress);
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Mock register batch resolution returning the wallet's public key
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeysBatchAsync(
                registerId,
                It.Is<BatchPublicKeyRequest>(r => r.WalletAddresses.Contains(walletAddress)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse
            {
                Resolved = new Dictionary<string, PublicKeyResolution>
                {
                    [walletAddress] = new PublicKeyResolution
                    {
                        ParticipantId = "participant-1",
                        ParticipantName = "Test Participant",
                        WalletAddress = walletAddress,
                        PublicKey = publicKeyBase64,
                        Algorithm = "ED25519",
                        Status = "active"
                    }
                },
                NotFound = [],
                Revoked = []
            });

        // Capture the DisclosureGroup[] passed to encryption pipeline to verify RecipientInfo
        DisclosureGroup[]? capturedGroups = null;
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DisclosureGroup[], CancellationToken>((groups, _) => capturedGroups = groups)
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups(walletAddress)));

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert
        result.Should().NotBeNull();
        capturedGroups.Should().NotBeNull();
        capturedGroups!.Length.Should().BeGreaterThan(0);

        var recipient = capturedGroups.SelectMany(g => g.Recipients).FirstOrDefault(r => r.WalletAddress == walletAddress);
        recipient.Should().NotBeNull();
        recipient!.Source.Should().Be(KeySource.Register);
        recipient.Algorithm.Should().Be(WalletNetworks.ED25519);
        recipient.PublicKey.Should().BeEquivalentTo(publicKeyBytes);
    }

    [Fact]
    public async Task ResolveRecipientKeys_ExternalProvidedKeys_TakePrecedenceOverRegister()
    {
        // Arrange
        var service = CreateService();
        var instanceId = "test-instance";
        var actionId = 1;
        var registerId = "register-1";
        var walletAddress = "wallet-recipient";

        var externalKeyBytes = new byte[32];
        new Random(99).NextBytes(externalKeyBytes);
        var externalKeyBase64 = Convert.ToBase64String(externalKeyBytes);

        var registerKeyBytes = new byte[32];
        new Random(77).NextBytes(registerKeyBytes);
        var registerKeyBase64 = Convert.ToBase64String(registerKeyBytes);

        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = walletAddress,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1"
            },
            // External key provided — should take precedence
            ExternalRecipientKeys = new Dictionary<string, ExternalKeyInfo>
            {
                [walletAddress] = new ExternalKeyInfo
                {
                    PublicKey = externalKeyBase64,
                    Algorithm = "ED25519"
                }
            }
        };

        var instance = CreateTestInstance(instanceId, "blueprint-1", registerId,
            new Dictionary<string, string> { ["applicant"] = walletAddress });
        var blueprint = CreateTestBlueprint(walletAddress);
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Register also has this wallet — but it should NOT be called because
        // the external key takes precedence (wallet is excluded from batch request)
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeysBatchAsync(
                registerId,
                It.IsAny<BatchPublicKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse
            {
                Resolved = new Dictionary<string, PublicKeyResolution>(),
                NotFound = [],
                Revoked = []
            });

        // Capture the DisclosureGroup[] passed to encryption pipeline
        DisclosureGroup[]? capturedGroups = null;
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DisclosureGroup[], CancellationToken>((groups, _) => capturedGroups = groups)
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups(walletAddress)));

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert
        result.Should().NotBeNull();
        capturedGroups.Should().NotBeNull();

        var recipient = capturedGroups!.SelectMany(g => g.Recipients).FirstOrDefault(r => r.WalletAddress == walletAddress);
        recipient.Should().NotBeNull();
        recipient!.Source.Should().Be(KeySource.External);
        recipient.PublicKey.Should().BeEquivalentTo(externalKeyBytes);

        // Verify that the batch resolution was either not called or called without the external-keyed wallet
        _mockRegisterClient.Verify(x => x.ResolvePublicKeysBatchAsync(
            registerId,
            It.Is<BatchPublicKeyRequest>(r => r.WalletAddresses.Contains(walletAddress)),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveRecipientKeys_MixedSources_BothResolvedCorrectly()
    {
        // Arrange
        var service = CreateService();
        var instanceId = "test-instance";
        var actionId = 1;
        var registerId = "register-1";
        var externalWallet = "wallet-external";
        var registerWallet = "wallet-register";

        var externalKeyBytes = new byte[32];
        new Random(11).NextBytes(externalKeyBytes);
        var externalKeyBase64 = Convert.ToBase64String(externalKeyBytes);

        var registerKeyBytes = new byte[32];
        new Random(22).NextBytes(registerKeyBytes);
        var registerKeyBase64 = Convert.ToBase64String(registerKeyBytes);

        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = externalWallet,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1"
            },
            // Only external wallet has a key; register wallet must come from register
            ExternalRecipientKeys = new Dictionary<string, ExternalKeyInfo>
            {
                [externalWallet] = new ExternalKeyInfo
                {
                    PublicKey = externalKeyBase64,
                    Algorithm = "ED25519"
                }
            }
        };

        var instance = CreateTestInstance(instanceId, "blueprint-1", registerId,
            new Dictionary<string, string>
            {
                ["applicant"] = externalWallet,
                ["reviewer"] = registerWallet
            });
        var blueprint = CreateTestBlueprintTwoParticipants(externalWallet, registerWallet);
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosureTwoWallets(blueprint, action, externalWallet, registerWallet);
        SetupFullTransactionFlow(instance);

        // Register returns key only for registerWallet
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeysBatchAsync(
                registerId,
                It.Is<BatchPublicKeyRequest>(r =>
                    r.WalletAddresses.Contains(registerWallet) &&
                    !r.WalletAddresses.Contains(externalWallet)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse
            {
                Resolved = new Dictionary<string, PublicKeyResolution>
                {
                    [registerWallet] = new PublicKeyResolution
                    {
                        ParticipantId = "participant-2",
                        ParticipantName = "Register Participant",
                        WalletAddress = registerWallet,
                        PublicKey = registerKeyBase64,
                        Algorithm = "ED25519",
                        Status = "active"
                    }
                },
                NotFound = [],
                Revoked = []
            });

        // Capture the DisclosureGroup[] passed to encryption pipeline
        DisclosureGroup[]? capturedGroups = null;
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DisclosureGroup[], CancellationToken>((groups, _) => capturedGroups = groups)
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups(externalWallet, registerWallet)));

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert
        result.Should().NotBeNull();
        capturedGroups.Should().NotBeNull();

        var allRecipients = capturedGroups!.SelectMany(g => g.Recipients).ToList();
        allRecipients.Should().HaveCount(2);

        var externalRecipient = allRecipients.First(r => r.WalletAddress == externalWallet);
        externalRecipient.Source.Should().Be(KeySource.External);
        externalRecipient.PublicKey.Should().BeEquivalentTo(externalKeyBytes);

        var registerRecipient = allRecipients.First(r => r.WalletAddress == registerWallet);
        registerRecipient.Source.Should().Be(KeySource.Register);
        registerRecipient.PublicKey.Should().BeEquivalentTo(registerKeyBytes);
    }

    [Fact]
    public async Task ResolveRecipientKeys_RevokedParticipant_FailsWithClearError()
    {
        // Arrange
        var service = CreateService();
        var instanceId = "test-instance";
        var actionId = 1;
        var registerId = "register-1";
        var revokedWallet = "wallet-revoked";

        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = revokedWallet,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1"
            }
            // No external keys — must resolve from register
        };

        var instance = CreateTestInstance(instanceId, "blueprint-1", registerId,
            new Dictionary<string, string> { ["applicant"] = revokedWallet });
        var blueprint = CreateTestBlueprint(revokedWallet);
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        // Register returns the wallet as revoked
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeysBatchAsync(
                registerId,
                It.Is<BatchPublicKeyRequest>(r => r.WalletAddresses.Contains(revokedWallet)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse
            {
                Resolved = new Dictionary<string, PublicKeyResolution>(),
                NotFound = [],
                Revoked = [revokedWallet]
            });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(instanceId, actionId, request, "test-token"));

        ex.Message.Should().Contain("revoked", because: "error should clearly indicate revocation");
        ex.Message.Should().Contain(revokedWallet, because: "error should identify the revoked wallet");
    }

    [Fact]
    public async Task ResolveRecipientKeys_NotFoundWithoutExternal_SkippedWithWarning()
    {
        // Arrange
        var service = CreateService();
        var instanceId = "test-instance";
        var actionId = 1;
        var registerId = "register-1";
        var knownWallet = "wallet-known";
        var unknownWallet = "wallet-unknown";

        var knownKeyBytes = new byte[32];
        new Random(55).NextBytes(knownKeyBytes);
        var knownKeyBase64 = Convert.ToBase64String(knownKeyBytes);

        var request = new ActionSubmissionRequest
        {
            BlueprintId = "blueprint-1",
            ActionId = "1",
            SenderWallet = knownWallet,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1"
            }
            // No external keys — both wallets must resolve from register
        };

        var instance = CreateTestInstance(instanceId, "blueprint-1", registerId,
            new Dictionary<string, string>
            {
                ["applicant"] = knownWallet,
                ["reviewer"] = unknownWallet
            });
        var blueprint = CreateTestBlueprintTwoParticipants(knownWallet, unknownWallet);
        var action = blueprint.Actions!.First(a => a.Id == actionId);

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosureTwoWallets(blueprint, action, knownWallet, unknownWallet);
        SetupFullTransactionFlow(instance);

        // Register returns one found, one not found
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeysBatchAsync(
                registerId,
                It.IsAny<BatchPublicKeyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchPublicKeyResponse
            {
                Resolved = new Dictionary<string, PublicKeyResolution>
                {
                    [knownWallet] = new PublicKeyResolution
                    {
                        ParticipantId = "participant-1",
                        ParticipantName = "Known Participant",
                        WalletAddress = knownWallet,
                        PublicKey = knownKeyBase64,
                        Algorithm = "ED25519",
                        Status = "active"
                    }
                },
                NotFound = [unknownWallet],
                Revoked = []
            });

        // Capture encryption pipeline call — should proceed with only the known wallet
        DisclosureGroup[]? capturedGroups = null;
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(
                It.IsAny<DisclosureGroup[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<DisclosureGroup[], CancellationToken>((groups, _) => capturedGroups = groups)
            .ReturnsAsync((DisclosureGroup[] groups, CancellationToken _) =>
            {
                // Pipeline reports skipped recipients
                return EncryptionResult.Succeeded(
                    CreateTestEncryptedGroups(knownWallet),
                    [unknownWallet]);
            });

        // Act
        var result = await service.ExecuteAsync(instanceId, actionId, request, "test-token");

        // Assert — should succeed despite unknown wallet
        result.Should().NotBeNull();
        result.TransactionId.Should().NotBeNullOrEmpty();

        // The unknown wallet should NOT be in any recipient list
        if (capturedGroups != null)
        {
            var allRecipientWallets = capturedGroups.SelectMany(g => g.Recipients).Select(r => r.WalletAddress);
            allRecipientWallets.Should().NotContain(unknownWallet);
        }

        // Verify warning was logged about skipped recipients
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Skipped") || v.ToString()!.Contains("not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Helper Methods

    #region Feature 137 — cross-node credential delivery-key precedence (FR-012 / SC-004)

    [Fact]
    public async Task SorchaLocalWallet_NoPublishedRecord_NoCarriedKey_FailsClosed_IssuesNoCredential()
    {
        // SC-004: when neither a published participant record nor carried keys resolve, the system
        // issues ZERO credentials. With HolderKeySourceField set and no holderKeys in the payload,
        // ExecuteAsync must fail closed before the Wallet Service is ever asked to mint.
        var service = CreateService();
        const string instanceId = "xnode-instance";
        const string registerId = "register-x";
        const string citizenWallet = "ws1qcitizen";

        var blueprint = CreateSorchaLocalWalletBlueprint(citizenWallet);
        var action = blueprint.Actions!.First(a => a.Id == 1);
        var instance = CreateTestInstance(instanceId, blueprint.Id, registerId,
            new Dictionary<string, string> { ["citizen"] = citizenWallet });

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        // Lets the starting-action prerequisite (blueprint-publish tx confirmation) pass so
        // execution reaches the step-8c credential-issuance precedence check.
        SetupFullTransactionFlow(instance);

        // Register has no published key for the open-participant citizen.
        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeyAsync(registerId, citizenWallet, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublicKeyResolution?)null);

        var request = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1",
            SenderWallet = citizenWallet,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object> { ["field1"] = "value1" } // no holderKeys
        };

        var act = () => service.ExecuteAsync(instanceId, 1, request, "test-token");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("VAL_RUNTIME_CRED_004");

        _mockWalletClient.Verify(x => x.IssueCredentialAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<bool>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<JsonElement?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "fail-closed must issue zero credentials (SC-004)");
    }

    [Fact]
    public async Task SorchaLocalWallet_CarriedKeys_BindCnf_AndInjectDeliveryKey_WhenRegisterMisses()
    {
        // FR-012/FR-014: register misses → the carried holder JWK binds cnf and the carried
        // encryption key is injected so the credential disclosure encrypts to the citizen.
        var service = CreateService();
        const string instanceId = "xnode-instance-2";
        const string registerId = "register-x2";
        const string citizenWallet = "ws1qcitizen2";

        var encKeyBytes = new byte[32];
        new Random(137).NextBytes(encKeyBytes);
        var encKeyBase64 = Convert.ToBase64String(encKeyBytes);

        var blueprint = CreateSorchaLocalWalletBlueprint(citizenWallet);
        var action = blueprint.Actions!.First(a => a.Id == 1);
        var instance = CreateTestInstance(instanceId, blueprint.Id, registerId,
            new Dictionary<string, string> { ["citizen"] = citizenWallet });

        SetupCommonMocks(instanceId, instance, blueprint, action);
        SetupRoutingAndDisclosure(blueprint, action);
        SetupFullTransactionFlow(instance);

        _mockRegisterClient
            .Setup(x => x.ResolvePublicKeyAsync(registerId, citizenWallet, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublicKeyResolution?)null);

        JsonElement? capturedHolderJwk = null;
        _mockWalletClient
            .Setup(x => x.IssueCredentialAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<JsonElement?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(new InvocationAction(inv => capturedHolderJwk = (JsonElement?)inv.Arguments[13]))
            .ReturnsAsync(new CredentialIssuanceResult
            {
                CredentialId = "urn:uuid:cred-1",
                Type = "AssuredIdentityCredential",
                IssuerDid = citizenWallet,
                SubjectDid = citizenWallet,
                Claims = new Dictionary<string, object>(),
                RawToken = "eyJ.tok.en",
                IssuedAt = DateTimeOffset.UtcNow
            });

        DisclosureGroup[]? capturedGroups = null;
        _mockEncryptionPipeline
            .Setup(x => x.EncryptDisclosedPayloadsAsync(It.IsAny<DisclosureGroup[]>(), It.IsAny<CancellationToken>()))
            .Callback<DisclosureGroup[], CancellationToken>((groups, _) => capturedGroups = groups)
            .ReturnsAsync(EncryptionResult.Succeeded(CreateTestEncryptedGroups(citizenWallet)));

        var request = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1",
            SenderWallet = citizenWallet,
            RegisterAddress = registerId,
            PayloadData = new Dictionary<string, object>
            {
                ["field1"] = "value1",
                ["holderKeys"] = new Dictionary<string, object>
                {
                    ["holderJwk"] = new Dictionary<string, object>
                    {
                        ["kty"] = "EC", ["crv"] = "P-256", ["x"] = "AAA", ["y"] = "BBB"
                    },
                    ["encryptionPublicKey"] = encKeyBase64,
                    ["algorithm"] = "ED25519"
                }
            }
        };

        await service.ExecuteAsync(instanceId, 1, request, "test-token");

        // cnf binding: the holder JWK was threaded to the Wallet Service issuance call.
        capturedHolderJwk.Should().NotBeNull("the carried holder JWK must bind the credential cnf (FR-014)");
        capturedHolderJwk!.Value.GetProperty("kty").GetString().Should().Be("EC");

        // delivery key: the carried encryption key was injected so the recipient resolves externally.
        capturedGroups.Should().NotBeNull();
        var recipient = capturedGroups!.SelectMany(g => g.Recipients).FirstOrDefault(r => r.WalletAddress == citizenWallet);
        recipient.Should().NotBeNull();
        recipient!.Source.Should().Be(KeySource.External);
        recipient.PublicKey.Should().BeEquivalentTo(encKeyBytes);
    }

    private static BlueprintModel CreateSorchaLocalWalletBlueprint(string citizenWallet)
    {
        return new BlueprintModel
        {
            Id = "blueprint-xnode",
            Title = "Cross-node Assured Identity",
            Participants =
            [
                // Open participant: walletAddress null in the published blueprint, late-bound at runtime.
                new ParticipantModel { Id = "citizen", Name = "Citizen", WalletAddress = null }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Issue Assured Identity",
                    Sender = "citizen",
                    IsStartingAction = true,
                    CredentialIssuanceConfig = new Sorcha.Blueprint.Models.Credentials.CredentialIssuanceConfig
                    {
                        CredentialType = "AssuredIdentityCredential",
                        TargetAudience = Sorcha.Blueprint.Models.Credentials.TargetAudience.SorchaLocalWallet,
                        RecipientParticipantId = "citizen",
                        HolderKeySourceField = "/holderKeys/holderJwk",
                        ClaimMappings = [new Sorcha.Blueprint.Models.Credentials.ClaimMapping { ClaimName = "field1", SourceField = "/field1" }]
                    },
                    Routes = [new RouteModel { NextActionIds = [] }]
                }
            ]
        };
    }

    #endregion

    private void SetupCommonMocks(string instanceId, Instance instance, BlueprintModel blueprint, ActionModel action)
    {
        _mockInstanceStore
            .Setup(x => x.GetAsync(instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockActionResolver
            .Setup(x => x.GetBlueprintAsync(instance.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        _mockActionResolver
            .Setup(x => x.GetActionDefinition(blueprint, action.Id.ToString()))
            .Returns(action);

        _mockStateReconstruction
            .Setup(x => x.ReconstructAsync(
                blueprint,
                instanceId,
                action.Id,
                instance.RegisterId,
                It.IsAny<string>(),
                instance.ParticipantWallets,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccumulatedState());

        _mockExecutionEngine
            .Setup(x => x.ValidateAsync(
                It.IsAny<Dictionary<string, object>>(),
                action,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.ValidationResult.Valid());

        _mockExecutionEngine
            .Setup(x => x.ApplyCalculationsAsync(
                It.IsAny<Dictionary<string, object>>(),
                action,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
    }

    private void SetupRoutingAndDisclosure(BlueprintModel blueprint, ActionModel action)
    {
        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingWithMappingAsync(
                blueprint,
                action,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<System.Text.Json.Nodes.JsonObject?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>());
    }

    private void SetupRoutingAndDisclosureTwoWallets(BlueprintModel blueprint, ActionModel action,
        string wallet1, string wallet2)
    {
        _mockExecutionEngine
            .Setup(x => x.DetermineRoutingWithMappingAsync(
                blueprint,
                action,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<System.Text.Json.Nodes.JsonObject?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Blueprint.Engine.Models.RoutingResult.Complete());

        // Return disclosure results for both wallets (using ParticipantId which maps to wallet via participantWallets)
        _mockExecutionEngine
            .Setup(x => x.ApplyDisclosures(It.IsAny<Dictionary<string, object>>(), action))
            .Returns(new List<Sorcha.Blueprint.Engine.Models.DisclosureResult>
            {
                Sorcha.Blueprint.Engine.Models.DisclosureResult.Create(
                    participantId: "applicant",
                    disclosedData: new Dictionary<string, object> { ["field1"] = "value1" }),
                Sorcha.Blueprint.Engine.Models.DisclosureResult.Create(
                    participantId: "reviewer",
                    disclosedData: new Dictionary<string, object> { ["field1"] = "value1" })
            });
    }

    private void SetupFullTransactionFlow(Instance instance)
    {
        _mockWalletClient
            .Setup(x => x.SignTransactionAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                SignedBy = "wallet-sender",
                Algorithm = "ED25519"
            });

        _mockValidatorClient
            .Setup(x => x.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345"
            });

        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "abc123def456abc123def456abc123def456abc123def456abc123def456abc12345",
                DocketNumber = 1
            });

        _mockInstanceStore
            .Setup(x => x.UpdateAsync(
                It.IsAny<Instance>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance inst, CancellationToken _) => inst);
    }

    private static Instance CreateTestInstance(string instanceId, string blueprintId, string registerId,
        Dictionary<string, string> participantWallets)
    {
        return new Instance
        {
            Id = instanceId,
            BlueprintId = blueprintId,
            BlueprintVersion = 1,
            RegisterId = registerId,
            TenantId = "test-tenant",
            State = InstanceState.Active,
            CurrentActionIds = [1],
            ParticipantWallets = participantWallets
        };
    }

    private static BlueprintModel CreateTestBlueprint(string senderWallet)
    {
        return new BlueprintModel
        {
            Id = "blueprint-1",
            Title = "Test Blueprint",
            Participants =
            [
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = senderWallet }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit Application",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Routes =
                    [
                        new RouteModel { NextActionIds = [2] }
                    ]
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review Application",
                    Sender = "applicant"
                }
            ]
        };
    }

    private static BlueprintModel CreateTestBlueprintTwoParticipants(string wallet1, string wallet2)
    {
        return new BlueprintModel
        {
            Id = "blueprint-1",
            Title = "Test Blueprint",
            Participants =
            [
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = wallet1 },
                new ParticipantModel { Id = "reviewer", Name = "Reviewer", WalletAddress = wallet2 }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit Application",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Routes =
                    [
                        new RouteModel { NextActionIds = [2] }
                    ]
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review Application",
                    Sender = "reviewer"
                }
            ]
        };
    }

    private static EncryptedPayloadGroup[] CreateTestEncryptedGroups(params string[] walletAddresses)
    {
        var wrappedKeys = walletAddresses.Select(w => new WrappedKey
        {
            WalletAddress = w,
            EncryptedKey = new byte[48],
            Algorithm = WalletNetworks.ED25519
        }).ToArray();

        return
        [
            new EncryptedPayloadGroup
            {
                GroupId = "test-group-id",
                DisclosedFields = ["field1"],
                Ciphertext = new byte[64],
                Nonce = new byte[12],
                PlaintextHash = new byte[32],
                EncryptionAlgorithm = EncryptionType.AES_GCM,
                WrappedKeys = wrappedKeys
            }
        ];
    }

    #endregion
}
