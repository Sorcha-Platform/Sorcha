// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

public class CryptoPolicyServiceTests
{
    private readonly Mock<IRegisterRepository> _repositoryMock;
    private readonly Mock<ISystemWalletSigningService> _signingMock = new();
    private readonly Mock<IValidatorServiceClient> _validatorMock = new();
    private readonly TransactionManager _transactionManager;
    private readonly CryptoPolicyService _sut;

    /// <summary>Captures the submission the service handed to the Validator, for assertion.</summary>
    private TransactionSubmission? _captured;

    public CryptoPolicyServiceTests()
    {
        _repositoryMock = new Mock<IRegisterRepository>();

        _signingMock
            .Setup(x => x.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = [1, 2, 3, 4],
                PublicKey = [5, 6, 7, 8],
                Algorithm = "ED25519",
                WalletAddress = "ws11qtestsystemwallet"
            });

        _validatorMock
            .Setup(x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback((TransactionSubmission s, CancellationToken _) => _captured = s)
            .ReturnsAsync(() => new TransactionSubmissionResult { Success = true });

        // The service now reads control transactions via the pushed-down GetTransactionsByTypeAsync.
        // Each test stubs the mock via GetTransactionsAsync, so derive the by-type result from that same
        // stub at call time (filter to Control, TimeStamp ascending, honour skip/take) — one setup covers
        // every case.
        _repositoryMock
            .Setup(x => x.GetTransactionsByTypeAsync(
                It.IsAny<string>(), TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (string rid, TransactionType _, TransactionSort _, int skip, int take, CancellationToken ct) =>
            {
                var all = await _repositoryMock.Object.GetTransactionsAsync(rid, ct);
                var q = all
                    .Where(t => t.MetaData != null && t.MetaData.TransactionType == TransactionType.Control)
                    .OrderBy(t => t.TimeStamp)
                    .Skip(skip);
                if (take > 0) q = q.Take(take);
                return (IReadOnlyList<TransactionModel>)q.ToList();
            });

        _transactionManager = new TransactionManager(
            _repositoryMock.Object,
            Mock.Of<IEventPublisher>());
        _sut = new CryptoPolicyService(
            _transactionManager,
            _signingMock.Object,
            _validatorMock.Object,
            Mock.Of<ILogger<CryptoPolicyService>>());
    }

    [Fact]
    public async Task GetActivePolicyAsync_NoPolicySet_ReturnsDefault()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransactionModel>().AsQueryable());

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(1);
        result.AcceptedSignatureAlgorithms.Should().NotBeEmpty();
        result.EnforcementMode.Should().Be(CryptoPolicyEnforcementMode.Permissive);
    }

    [Fact]
    public async Task GetActivePolicyAsync_GenesisHasPolicy_ReturnsGenesisPolicy()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";
        var policy = new CryptoPolicy
        {
            Version = 2,
            AcceptedSignatureAlgorithms = new[] { "ED25519", "ML-DSA-65" },
            RequiredSignatureAlgorithms = new[] { "ED25519" },
            EnforcementMode = CryptoPolicyEnforcementMode.Strict
        };

        var controlRecord = new RegisterControlRecord
        {
            RegisterId = registerId,
            Name = "Test",

            CreatedAt = DateTimeOffset.UtcNow,
            CryptoPolicy = policy,
            Attestations = new List<RegisterAttestation>()
        };

        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(controlRecord)));

        var genesisTx = CreateControlTx(registerId, payload, DateTime.UtcNow.AddDays(-1));

        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { genesisTx }.AsQueryable());

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(2);
        result.EnforcementMode.Should().Be(CryptoPolicyEnforcementMode.Strict);
        result.AcceptedSignatureAlgorithms.Should().Contain("ML-DSA-65");
    }

    [Fact]
    public async Task GetActivePolicyAsync_PolicyUpdateExists_ReturnsLatestUpdate()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";

        var updatePolicy = new CryptoPolicy
        {
            Version = 3,
            AcceptedSignatureAlgorithms = new[] { "ED25519", "ML-DSA-65", "SLH-DSA-128s" },
            RequiredSignatureAlgorithms = new[] { "ML-DSA-65" },
            EnforcementMode = CryptoPolicyEnforcementMode.Strict
        };

        var updateTx = CreatePolicyUpdateTx(registerId, updatePolicy, DateTime.UtcNow);

        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { updateTx }.AsQueryable());

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(3);
        result.AcceptedSignatureAlgorithms.Should().HaveCount(3);
        result.RequiredSignatureAlgorithms.Should().Contain("ML-DSA-65");
    }

    [Fact]
    public async Task GetPolicyHistoryAsync_MultiplePolicies_ReturnsOrderedByVersion()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";

        var policy1 = new CryptoPolicy
        {
            Version = 1,
            AcceptedSignatureAlgorithms = new[] { "ED25519" },
            EnforcementMode = CryptoPolicyEnforcementMode.Permissive
        };
        var policy2 = new CryptoPolicy
        {
            Version = 2,
            AcceptedSignatureAlgorithms = new[] { "ED25519", "ML-DSA-65" },
            EnforcementMode = CryptoPolicyEnforcementMode.Strict
        };

        var tx1 = CreatePolicyUpdateTx(registerId, policy1, DateTime.UtcNow.AddDays(-2));
        var tx2 = CreatePolicyUpdateTx(registerId, policy2, DateTime.UtcNow.AddDays(-1));

        // GetPolicyHistoryAsync calls GetTransactionsAsync multiple times
        // Return fresh queryable each call
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<IQueryable<TransactionModel>>(new[] { tx1, tx2 }.AsQueryable()));

        // Act
        var result = await _sut.GetPolicyHistoryAsync(registerId);

        // Assert — both are updates (not genesis), so all come from FindAllPolicyUpdatesAsync
        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result.First().Version.Should().Be(1);
        result.Last().Version.Should().Be(2);
    }

    [Fact]
    public async Task GetActivePolicyAsync_ExceptionThrown_ReturnsDefault()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB failure"));

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(1); // Default
    }

    private static TransactionModel CreateControlTx(
        string registerId, string payloadData, DateTime timestamp)
    {
        return new TransactionModel
        {
            TxId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            SenderWallet = "system",
            Signature = "test",
            PayloadCount = 1,
            TimeStamp = timestamp,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = payloadData,
                    Hash = "test",
                    WalletAccess = Array.Empty<string>()
                }
            },
            MetaData = new TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = TransactionType.Control
            }
        };
    }

    private static TransactionModel CreatePolicyUpdateTx(
        string registerId, CryptoPolicy policy, DateTime timestamp)
    {
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(policy)));

        return new TransactionModel
        {
            TxId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            SenderWallet = "system",
            Signature = "test",
            PayloadCount = 1,
            TimeStamp = timestamp,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = payload,
                    Hash = "test",
                    WalletAccess = Array.Empty<string>()
                }
            },
            MetaData = new TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = TransactionType.Control,
                TrackingData = new Dictionary<string, string>
                {
                    ["transactionType"] = "CryptoPolicyUpdate"
                }
            }
        };
    }

    // ------------------------------------------------------------------
    // SubmitPolicyUpdateAsync — the DevMode→Normal promotion path.
    //
    // These guard a defect that shipped green: the endpoint wrote the control transaction
    // straight to the register store, so it never entered the Validator's verified queue,
    // never sealed into a docket, and the register's DevMode flag never flipped. The endpoint
    // still returned 200. Nothing asserted the join, so nothing failed.
    // ------------------------------------------------------------------

    private const string ProbeRegisterId = "c794c86cfb82428994c95668d19107f7";

    private static readonly JsonSerializerOptions ValidatorCanonicalOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static CryptoPolicy PromotionPolicy()
    {
        var policy = CryptoPolicy.CreateDefault();
        policy.Version = 2;
        policy.DevMode = false;
        return policy;
    }

    private void NoExistingTransactions(string registerId) =>
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransactionModel>().AsQueryable());

    [Fact]
    public async Task SubmitPolicyUpdateAsync_SubmitsThroughValidator_SoItCanSeal()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // The whole point. A direct store write is invisible to DocketBuilder, which claims
        // only from IVerifiedTransactionQueue — so it never seals and the promotion never happens.
        _validatorMock.Verify(
            x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_CarriesTheActionIdTheValidatorMatchesOn()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // ControlDocketProcessor.GetControlActionType matches this exact string to recognise the
        // transaction as a crypto-policy update. Any other value and the transaction still seals,
        // but as an ordinary action — the policy is silently never applied.
        _captured!.ActionId.Should().Be("control.crypto.update");
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_CarriesANonEmptyNonGenesisBlueprintId()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // Both halves were found by live execution, not by this suite's mock validator.
        // Empty  => TransactionValidator rejects with TX_003 "Blueprint ID is required".
        // "genesis" => TransactionTypeClassifier.IsGenesisTransaction matches on exactly that
        // value, so a routine policy update would be treated as the network's pre-signed genesis
        // transaction and judged against the short GenesisMaxAge freshness window.
        _captured!.BlueprintId.Should().NotBeNullOrWhiteSpace();
        _captured.BlueprintId.Should().NotBe("genesis");
        _captured.BlueprintId.Should().Be("register-governance-v1");
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_CarriesTheMetadataTheDevModeProjectionRequires()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // Register Service's docket-write path flips Register.DevMode only when BOTH hold:
        // MetaData.TransactionType == Control (resolved from Metadata["Type"]) and
        // TrackingData["transactionType"] == "CryptoPolicyUpdate" (copied wholesale from Metadata).
        // Drop either and the promotion seals but never takes effect.
        _captured!.Metadata.Should().ContainKey("Type").WhoseValue.Should().Be("Control");
        _captured.Metadata.Should().ContainKey("transactionType")
            .WhoseValue.Should().Be("CryptoPolicyUpdate");
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_PayloadHashMatchesWhatTheValidatorRecomputes()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // ValidationEngine.ValidatePayloadHash re-canonicalises the payload and recomputes the
        // hash. A divergence is a fatal VAL_HASH_001 rejection that surfaces nowhere near here.
        var canonical = JsonSerializer.Serialize(_captured!.Payload, ValidatorCanonicalOptions);
        var expected = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        _captured.PayloadHash.Should().Be(expected);
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_PayloadDeserialisesAsTheValidatorsPolicyShape()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // The Validator parses this as CryptoPolicyUpdatePayload with camelCase naming.
        // devMode above all — that is the field the one-way guard reads.
        var payload = _captured!.Payload;
        payload.GetProperty("version").GetInt32().Should().Be(2);
        payload.GetProperty("devMode").GetBoolean().Should().BeFalse();
        payload.GetProperty("acceptedSignatureAlgorithms").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_SignsWithBase64Url_NotPlainBase64()
    {
        NoExistingTransactions(ProbeRegisterId);

        await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // Every sibling control-transaction producer encodes base64url and the Validator decodes
        // base64url. Plain base64 differs only for some byte values, so getting this wrong fails
        // intermittently and reads as a signature bug rather than an encoding one.
        var signature = _captured!.Signatures.Single();
        signature.SignatureValue.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        signature.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_RefusesToReEnableDevMode()
    {
        NoExistingTransactions(ProbeRegisterId);
        var downgrade = CryptoPolicy.CreateDefault();
        downgrade.Version = 3;
        downgrade.DevMode = true;

        var act = () => _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, downgrade);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*one-way*");
        _validatorMock.Verify(
            x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_ValidatorRejection_ThrowsRatherThanReportingSuccess()
    {
        NoExistingTransactions(ProbeRegisterId);
        _validatorMock
            .Setup(x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = false, ErrorMessage = "VAL_CHAIN_001" });

        var act = () => _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*VAL_CHAIN_001*");
    }

    [Fact]
    public async Task SubmitPolicyUpdateAsync_TransactionIdIsDeterministic_SoRetryIsIdempotent()
    {
        NoExistingTransactions(ProbeRegisterId);

        var first = await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());
        var second = await _sut.SubmitPolicyUpdateAsync(ProbeRegisterId, PromotionPolicy());

        // A retry must re-submit the same transaction id rather than forking the chain with a
        // second, competing policy update at the same version.
        second.TransactionId.Should().Be(first.TransactionId);
        first.PolicyVersion.Should().Be(2u);
    }
}
