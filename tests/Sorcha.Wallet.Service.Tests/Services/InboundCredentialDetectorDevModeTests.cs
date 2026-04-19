// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Tests.Helpers;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 106 Wave F follow-up — end-to-end <see cref="InboundCredentialDetector.TryExtractAsync"/>
/// tests that exercise the DevMode plaintext path introduced for the HaipVerifiedCitizen
/// walkthrough. The citizen applicant is anonymous/late-bound and cannot have their key
/// published, so the encryption pipeline falls through to plaintext. The detector must
/// parse that plaintext shape ONLY when the register is in DevMode — a production register
/// emitting plaintext is a security signal (encryption expected but recipient keys missing)
/// and we must never persist credentials off it.
/// </summary>
public class InboundCredentialDetectorDevModeTests
{
    private const string WalletAddress = "ws11qcitizen";
    private const string RegisterId = "af7b1040-register-devmode";
    private const string TransactionId = "tx-devmode-plaintext-1";

    private const string PlaintextTxBody = /*lang=json,strict*/ """
    {
      "type": "action",
      "blueprintId": "haip-verified-citizen-v2-1.0.0",
      "actionId": "2",
      "instanceId": "instance-devmode-round-trip",
      "previousTxId": "prev-tx-id",
      "timestamp": "2026-04-19T12:00:00+00:00",
      "payloads": {
        "ws11qcitizen": {
          "/credential": {
            "credentialId": "urn:uuid:feature-106-devmode-test-1",
            "credentialType": "VerifiedCitizenCredential",
            "issuerDid": "did:sorcha:org:ws11qgovernment",
            "issuerOrgName": "Government Identity Authority",
            "subjectDid": "did:sorcha:wallet:ws11qcitizen",
            "issuedAt": "2026-04-15T10:30:00+00:00",
            "expiresAt": "2027-04-15T10:30:00+00:00",
            "rawToken": "eyJhbGciOiJFZERTQSJ9.devmode.token",
            "issuanceBlueprintId": "haip-verified-citizen-v2-1.0.0",
            "issuanceInstanceId": "instance-devmode-round-trip",
            "issuanceActionId": "2",
            "claimActionId": "3",
            "registerId": "af7b1040-register-devmode"
          }
        }
      }
    }
    """;

    [Fact]
    public async Task TryExtractAsync_DevMode_PlaintextPayload_PersistsPendingCredential()
    {
        var fixture = new Fixture();
        fixture.ArrangePlaintextTransaction(PlaintextTxBody);
        fixture.ArrangeRegisterDevMode(true);

        var extract = await fixture.Sut.TryExtractAsync(
            WalletAddress, TransactionId, RegisterId, CancellationToken.None);

        extract.Should().NotBeNull("DevMode register + plaintext /credential payload → detector must extract");
        extract!.CredentialId.Should().Be("urn:uuid:feature-106-devmode-test-1");
        extract.RawToken.Should().Be("eyJhbGciOiJFZERTQSJ9.devmode.token");

        fixture.CredentialStoreMock.Verify(
            s => s.StoreAsync(It.Is<CredentialEntity>(e =>
                e.Id == "urn:uuid:feature-106-devmode-test-1"
                && e.Status == CredentialStatus.PendingAcceptance
                && e.WalletAddress == WalletAddress),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryExtractAsync_NonDevMode_PlaintextPayload_Skips()
    {
        // Security posture: plaintext on a non-DevMode register only happens when the
        // encryption pipeline couldn't resolve recipient keys (Feature 083 gap). Persisting
        // such a credential would silently downgrade the DAD guarantee — must skip.
        var fixture = new Fixture();
        fixture.ArrangePlaintextTransaction(PlaintextTxBody);
        fixture.ArrangeRegisterDevMode(false);

        var extract = await fixture.Sut.TryExtractAsync(
            WalletAddress, TransactionId, RegisterId, CancellationToken.None);

        extract.Should().BeNull();
        fixture.CredentialStoreMock.Verify(
            s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "non-DevMode plaintext must never persist a credential");
    }

    [Fact]
    public async Task TryExtractAsync_RegisterLookupThrows_Skips()
    {
        // Transient Register Service failure while resolving DevMode flag → safer to
        // skip than persist. The bloom-filter hit will eventually retry once the
        // Register Service recovers.
        var fixture = new Fixture();
        fixture.ArrangePlaintextTransaction(PlaintextTxBody);
        fixture.RegisterClientMock
            .Setup(c => c.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("register service transient fault"));

        var extract = await fixture.Sut.TryExtractAsync(
            WalletAddress, TransactionId, RegisterId, CancellationToken.None);

        extract.Should().BeNull();
        fixture.CredentialStoreMock.Verify(
            s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryExtractAsync_DevMode_PlaintextForOtherWallet_Skips()
    {
        // The transaction targets ws11qcitizen but our detector is running for a
        // different wallet — there should be no match and nothing persisted.
        var fixture = new Fixture();
        fixture.ArrangePlaintextTransaction(PlaintextTxBody);
        fixture.ArrangeRegisterDevMode(true);

        var extract = await fixture.Sut.TryExtractAsync(
            "ws11qdifferent-wallet", TransactionId, RegisterId, CancellationToken.None);

        extract.Should().BeNull();
        fixture.CredentialStoreMock.Verify(
            s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryExtractAsync_DevMode_Duplicate_Skips()
    {
        // Idempotency invariant INV-1: a credential id that already exists in the
        // local store is a duplicate arrival; do not re-persist.
        var fixture = new Fixture();
        fixture.ArrangePlaintextTransaction(PlaintextTxBody);
        fixture.ArrangeRegisterDevMode(true);
        fixture.CredentialStoreMock
            .Setup(s => s.GetByIdAsync("urn:uuid:feature-106-devmode-test-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialEntity
            {
                Id = "urn:uuid:feature-106-devmode-test-1",
                Type = "VerifiedCitizenCredential",
                IssuerDid = "did:sorcha:org:ws11qgovernment",
                SubjectDid = "did:sorcha:wallet:ws11qcitizen",
                ClaimsJson = "{}",
                RawToken = "eyJhbGciOiJFZERTQSJ9.devmode.token",
                IssuedAt = DateTimeOffset.UtcNow,
                Status = CredentialStatus.PendingAcceptance,
                IssuanceTxId = TransactionId,
                WalletAddress = WalletAddress,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var extract = await fixture.Sut.TryExtractAsync(
            WalletAddress, TransactionId, RegisterId, CancellationToken.None);

        extract.Should().BeNull();
        fixture.CredentialStoreMock.Verify(
            s => s.StoreAsync(It.IsAny<CredentialEntity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Fixture: minimal detector wiring — only the Register Service client and
    // credential store behaviours matter on the plaintext path; the WalletManager
    // and ISymmetricCrypto are never invoked but are required by the constructor.
    // -----------------------------------------------------------------------

    private sealed class Fixture
    {
        public Mock<IRegisterServiceClient> RegisterClientMock { get; }
        public Mock<ICredentialStore> CredentialStoreMock { get; }
        public InboundCredentialDetector Sut { get; }

        public Fixture()
        {
            RegisterClientMock = new Mock<IRegisterServiceClient>();
            CredentialStoreMock = new Mock<ICredentialStore>();
            var symmetricCryptoMock = new Mock<ISymmetricCrypto>();

            // WalletManager is constructor-required but unreachable on the plaintext
            // path — build a stub with in-memory repository + noop dependencies.
            var walletRepository = new InMemoryWalletRepository();
            var encryptionProvider = new LocalEncryptionProvider(NullLogger<LocalEncryptionProvider>.Instance);
            var eventPublisher = new InMemoryEventPublisher(NullLogger<InMemoryEventPublisher>.Instance);
            var keyManagement = new KeyManagementService(
                encryptionProvider,
                Mock.Of<ICryptoModule>(),
                Mock.Of<IWalletUtilities>(),
                NullLogger<KeyManagementService>.Instance);
            var transactionService = new TransactionService(
                Mock.Of<ICryptoModule>(),
                Mock.Of<IHashProvider>(),
                NullLogger<TransactionService>.Instance);
            var delegationService = new DelegationService(
                walletRepository,
                NullLogger<DelegationService>.Instance);
            var walletManager = new WalletManager(
                keyManagement,
                transactionService,
                delegationService,
                walletRepository,
                eventPublisher,
                NullLogger<WalletManager>.Instance,
                Mock.Of<IRecoveryKeyService>());

            var metrics = new InboundCredentialDetectorMetrics(new TestMeterFactory());

            Sut = new InboundCredentialDetector(
                RegisterClientMock.Object,
                walletManager,
                symmetricCryptoMock.Object,
                CredentialStoreMock.Object,
                metrics,
                NullLogger<InboundCredentialDetector>.Instance);
        }

        public void ArrangePlaintextTransaction(string canonicalJson)
        {
            var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(canonicalJson));
            var tx = new TransactionModel
            {
                Id = TransactionId,
                Payloads = [new PayloadModel { Data = data }],
            };

            RegisterClientMock
                .Setup(c => c.GetTransactionAsync(RegisterId, TransactionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tx);
        }

        public void ArrangeRegisterDevMode(bool devMode)
        {
            RegisterClientMock
                .Setup(c => c.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sorcha.Register.Models.Register
                {
                    Id = RegisterId,
                    Name = "HAIP Verified Citizen Register",
                    DevMode = devMode,
                });
        }
    }
}
