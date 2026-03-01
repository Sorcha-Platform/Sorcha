// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Tests.Encryption;

/// <summary>
/// Unit tests for <see cref="EncryptionPipelineService"/>.
/// Covers envelope encryption orchestration including single/multi-recipient,
/// multi-group, empty payload, and failure scenarios.
/// </summary>
public class EncryptionPipelineServiceTests
{
    private readonly Mock<ISymmetricCrypto> _symmetricCryptoMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly Mock<IHashProvider> _hashProviderMock;
    private readonly Mock<ILogger<EncryptionPipelineService>> _loggerMock;
    private readonly EncryptionPipelineService _sut;

    // Stable test data
    private static readonly byte[] FakeSymmetricKey = new byte[32];
    private static readonly byte[] FakeNonce = new byte[24];
    private static readonly byte[] FakeCiphertext = [0xDE, 0xAD, 0xBE, 0xEF];
    private static readonly byte[] FakePlaintextHash = new byte[32];
    private static readonly byte[] FakeWrappedKey = [0xCA, 0xFE, 0xBA, 0xBE];

    public EncryptionPipelineServiceTests()
    {
        _symmetricCryptoMock = new Mock<ISymmetricCrypto>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        _hashProviderMock = new Mock<IHashProvider>();
        _loggerMock = new Mock<ILogger<EncryptionPipelineService>>();

        // Default: hash provider returns a stable 32-byte hash
        _hashProviderMock
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(FakePlaintextHash);

        // Default: symmetric encryption succeeds
        _symmetricCryptoMock
            .Setup(s => s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<SymmetricCiphertext>.Success(
                new SymmetricCiphertext
                {
                    Data = FakeCiphertext,
                    Key = FakeSymmetricKey,
                    IV = FakeNonce,
                    Type = EncryptionType.XCHACHA20_POLY1305
                }));

        // Default: asymmetric key wrapping succeeds
        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(FakeWrappedKey));

        _sut = new EncryptionPipelineService(
            _symmetricCryptoMock.Object,
            _cryptoModuleMock.Object,
            _hashProviderMock.Object,
            _loggerMock.Object);
    }

    #region Single group, single recipient

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_SingleGroupOneRecipient_ReturnsCiphertextNonceWrappedKeyAndHash()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("group1", ["/name", "/age"],
                new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30 },
                CreateRecipient("ws1qrecip1", WalletNetworks.ED25519))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().HaveCount(1);

        var group = result.Groups[0];
        group.GroupId.Should().Be("group1");
        group.DisclosedFields.Should().BeEquivalentTo(["/name", "/age"]);
        group.Ciphertext.Should().BeEquivalentTo(FakeCiphertext);
        group.Nonce.Should().BeEquivalentTo(FakeNonce);
        group.PlaintextHash.Should().BeEquivalentTo(FakePlaintextHash);
        group.EncryptionAlgorithm.Should().Be(EncryptionType.XCHACHA20_POLY1305);
        group.WrappedKeys.Should().HaveCount(1);
        group.WrappedKeys[0].WalletAddress.Should().Be("ws1qrecip1");
        group.WrappedKeys[0].EncryptedKey.Should().BeEquivalentTo(FakeWrappedKey);
        group.WrappedKeys[0].Algorithm.Should().Be(WalletNetworks.ED25519);
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_SingleGroupOneRecipient_ComputesHashFromSerializedPayload()
    {
        // Arrange
        var payload = new Dictionary<string, object> { ["field"] = "value" };
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/field"], payload,
                CreateRecipient("ws1qa", WalletNetworks.NISTP256))
        };

        // Act
        await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — verify hash was computed on serialized bytes
        _hashProviderMock.Verify(h =>
            h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256), Times.Once);
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_SingleGroupOneRecipient_WrapsSymmetricKeyWithRecipientPublicKey()
    {
        // Arrange
        var recipientPubKey = new byte[] { 1, 2, 3, 4, 5 };
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "secret" },
                new RecipientInfo
                {
                    WalletAddress = "ws1qr1",
                    PublicKey = recipientPubKey,
                    Algorithm = WalletNetworks.ED25519,
                    Source = KeySource.Register
                })
        };

        // Act
        await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — verify asymmetric wrap was called with symmetric key and recipient's key
        _cryptoModuleMock.Verify(c =>
            c.EncryptAsync(
                It.IsAny<byte[]>(),
                (byte)WalletNetworks.ED25519,
                recipientPubKey,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Single group, multiple recipients

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_SingleGroupThreeRecipients_ReturnsOneCiphertextThreeWrappedKeys()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name"],
                new Dictionary<string, object> { ["name"] = "Test" },
                CreateRecipient("ws1qr1", WalletNetworks.ED25519),
                CreateRecipient("ws1qr2", WalletNetworks.NISTP256),
                CreateRecipient("ws1qr3", WalletNetworks.RSA4096))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().HaveCount(1);
        result.Groups[0].WrappedKeys.Should().HaveCount(3);

        // Symmetric encryption should happen exactly once per group (not per recipient)
        _symmetricCryptoMock.Verify(s =>
            s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Asymmetric wrapping should happen once per recipient
        _cryptoModuleMock.Verify(c =>
            c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_ThreeRecipients_AllWrappedKeysHaveCorrectWalletAddresses()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "val" },
                CreateRecipient("ws1qalice", WalletNetworks.ED25519),
                CreateRecipient("ws1qbob", WalletNetworks.NISTP256),
                CreateRecipient("ws1qcarol", WalletNetworks.RSA4096))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        var wallets = result.Groups[0].WrappedKeys.Select(w => w.WalletAddress).ToArray();
        wallets.Should().Contain("ws1qalice");
        wallets.Should().Contain("ws1qbob");
        wallets.Should().Contain("ws1qcarol");
    }

    #endregion

    #region Multiple groups

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_MultipleGroups_ReturnsCorrectNumberOfEncryptedGroups()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name"],
                new Dictionary<string, object> { ["name"] = "Alice" },
                CreateRecipient("ws1qr1", WalletNetworks.ED25519)),
            CreateDisclosureGroup("g2", ["/name", "/age"],
                new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30 },
                CreateRecipient("ws1qr2", WalletNetworks.NISTP256)),
            CreateDisclosureGroup("g3", ["/ssn"],
                new Dictionary<string, object> { ["ssn"] = "123-45-6789" },
                CreateRecipient("ws1qr3", WalletNetworks.RSA4096))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().HaveCount(3);
        result.Groups[0].GroupId.Should().Be("g1");
        result.Groups[1].GroupId.Should().Be("g2");
        result.Groups[2].GroupId.Should().Be("g3");

        // Symmetric encryption once per group
        _symmetricCryptoMock.Verify(s =>
            s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    #endregion

    #region Plaintext hash verification

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_PlaintextHash_IsSha256OfSerializedPayload()
    {
        // Arrange — set up specific hash return
        var expectedHash = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
                                        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
                                        0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20 };
        _hashProviderMock
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(expectedHash);

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/field"],
                new Dictionary<string, object> { ["field"] = "value" },
                CreateRecipient("ws1q1", WalletNetworks.ED25519))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups[0].PlaintextHash.Should().BeEquivalentTo(expectedHash);
    }

    #endregion

    #region Empty payload (T025)

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_EmptyDisclosureGroups_ReturnsSuccessWithEmptyGroups()
    {
        // Arrange
        var groups = Array.Empty<DisclosureGroup>();

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().BeEmpty();

        // Should not call any crypto operations
        _symmetricCryptoMock.Verify(s =>
            s.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<EncryptionType>(),
                It.IsAny<byte[]?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_AllGroupsHaveEmptyPayloads_ReturnsSuccessWithEmptyGroups()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name"],
                new Dictionary<string, object>(),
                CreateRecipient("ws1q1", WalletNetworks.ED25519)),
            CreateDisclosureGroup("g2", ["/age"],
                new Dictionary<string, object>(),
                CreateRecipient("ws1q2", WalletNetworks.NISTP256))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_NullDisclosureGroups_ReturnsSuccessWithEmptyGroups()
    {
        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(null!);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().BeEmpty();
    }

    #endregion

    #region Cryptographic failure atomicity (T026)

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_KeyWrappingFailure_FailsEntireOperationWithRecipientIdentified()
    {
        // Arrange — second recipient's key wrapping fails
        var failingPubKey = new byte[] { 0xFF, 0xFE, 0xFD };
        var callCount = 0;
        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 2)
                {
                    return CryptoResult<byte[]>.Failure(
                        CryptoStatus.EncryptionFailed,
                        "Public key invalid for encryption");
                }
                return CryptoResult<byte[]>.Success(FakeWrappedKey);
            });

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "secret" },
                CreateRecipient("ws1qalice", WalletNetworks.ED25519),
                CreateRecipient("ws1qbob_fail", WalletNetworks.NISTP256),
                CreateRecipient("ws1qcarol", WalletNetworks.RSA4096))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — entire operation fails, identifying the failing recipient
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.FailedRecipient.Should().Be("ws1qbob_fail");
        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_SymmetricEncryptionFailure_FailsEntireOperation()
    {
        // Arrange
        _symmetricCryptoMock
            .Setup(s => s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<SymmetricCiphertext>.Failure(
                CryptoStatus.EncryptionFailed,
                "Symmetric encryption engine failure"));

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "secret" },
                CreateRecipient("ws1qr1", WalletNetworks.ED25519))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Symmetric encryption failed");
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_KeyWrappingFailsOnSecondGroup_FailsEntireOperation()
    {
        // Arrange — first group succeeds, second group's key wrapping fails
        var groupCallCount = 0;
        _symmetricCryptoMock
            .Setup(s => s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                groupCallCount++;
                return CryptoResult<SymmetricCiphertext>.Success(
                    new SymmetricCiphertext
                    {
                        Data = FakeCiphertext,
                        Key = (byte[])FakeSymmetricKey.Clone(),
                        IV = (byte[])FakeNonce.Clone(),
                        Type = EncryptionType.XCHACHA20_POLY1305
                    });
            });

        var wrapCallCount = 0;
        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                wrapCallCount++;
                // Fail on second group's recipient (second call)
                if (wrapCallCount == 2)
                {
                    return CryptoResult<byte[]>.Failure(
                        CryptoStatus.EncryptionFailed,
                        "Key wrap failed");
                }
                return CryptoResult<byte[]>.Success(FakeWrappedKey);
            });

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name"],
                new Dictionary<string, object> { ["name"] = "Alice" },
                CreateRecipient("ws1qr1", WalletNetworks.ED25519)),
            CreateDisclosureGroup("g2", ["/ssn"],
                new Dictionary<string, object> { ["ssn"] = "123" },
                CreateRecipient("ws1qr2_fail", WalletNetworks.NISTP256))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — entire operation fails (atomicity), no partial results
        result.Success.Should().BeFalse();
        result.FailedRecipient.Should().Be("ws1qr2_fail");
        result.Groups.Should().BeEmpty();
    }

    #endregion

    #region All recipients get matching wrapped keys

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_EachRecipientGetsWrappedKeyMatchingTheirWalletAddress()
    {
        // Arrange — return different wrapped keys per algorithm so we can verify mapping
        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(
                It.IsAny<byte[]>(),
                (byte)WalletNetworks.ED25519,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(new byte[] { 0x01 }));

        _cryptoModuleMock
            .Setup(c => c.EncryptAsync(
                It.IsAny<byte[]>(),
                (byte)WalletNetworks.NISTP256,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(new byte[] { 0x02 }));

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "test" },
                CreateRecipient("ws1qed25519", WalletNetworks.ED25519),
                CreateRecipient("ws1qp256", WalletNetworks.NISTP256))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        var keys = result.Groups[0].WrappedKeys;
        keys.Should().HaveCount(2);

        var edKey = keys.First(k => k.WalletAddress == "ws1qed25519");
        edKey.EncryptedKey.Should().BeEquivalentTo(new byte[] { 0x01 });
        edKey.Algorithm.Should().Be(WalletNetworks.ED25519);

        var p256Key = keys.First(k => k.WalletAddress == "ws1qp256");
        p256Key.EncryptedKey.Should().BeEquivalentTo(new byte[] { 0x02 });
        p256Key.Algorithm.Should().Be(WalletNetworks.NISTP256);
    }

    #endregion

    #region EstimateEncryptedSize

    [Fact]
    public void EstimateEncryptedSize_SingleGroupOneRecipient_ReturnsReasonableEstimate()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name"],
                new Dictionary<string, object> { ["name"] = "Alice" },
                CreateRecipient("ws1q1", WalletNetworks.ED25519))
        };

        // Act
        var estimate = _sut.EstimateEncryptedSize(groups);

        // Assert — should be positive and include overhead
        estimate.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateEncryptedSize_EmptyGroups_ReturnsZero()
    {
        // Arrange
        var groups = Array.Empty<DisclosureGroup>();

        // Act
        var estimate = _sut.EstimateEncryptedSize(groups);

        // Assert
        estimate.Should().Be(0);
    }

    [Fact]
    public void EstimateEncryptedSize_RSA4096Recipient_IncludesLargerKeyOverhead()
    {
        // Arrange
        var payload = new Dictionary<string, object> { ["data"] = "test" };
        var ed25519Group = new[]
        {
            CreateDisclosureGroup("g1", ["/data"], payload,
                CreateRecipient("ws1q1", WalletNetworks.ED25519))
        };
        var rsa4096Group = new[]
        {
            CreateDisclosureGroup("g1", ["/data"], payload,
                CreateRecipient("ws1q1", WalletNetworks.RSA4096))
        };

        // Act
        var edEstimate = _sut.EstimateEncryptedSize(ed25519Group);
        var rsaEstimate = _sut.EstimateEncryptedSize(rsa4096Group);

        // Assert — RSA4096 should be larger due to 512-byte wrapped key vs 80-byte
        rsaEstimate.Should().BeGreaterThan(edEstimate);
    }

    [Fact]
    public void EstimateEncryptedSize_MultipleRecipients_ScalesWithRecipientCount()
    {
        // Arrange
        var payload = new Dictionary<string, object> { ["data"] = "test" };
        var oneRecipient = new[]
        {
            CreateDisclosureGroup("g1", ["/data"], payload,
                CreateRecipient("ws1q1", WalletNetworks.ED25519))
        };
        var threeRecipients = new[]
        {
            CreateDisclosureGroup("g1", ["/data"], payload,
                CreateRecipient("ws1q1", WalletNetworks.ED25519),
                CreateRecipient("ws1q2", WalletNetworks.ED25519),
                CreateRecipient("ws1q3", WalletNetworks.ED25519))
        };

        // Act
        var oneEst = _sut.EstimateEncryptedSize(oneRecipient);
        var threeEst = _sut.EstimateEncryptedSize(threeRecipients);

        // Assert — three recipients should be larger
        threeEst.Should().BeGreaterThan(oneEst);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "test" },
                CreateRecipient("ws1q1", WalletNetworks.ED25519))
        };

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.EncryptDisclosedPayloadsAsync(groups, cts.Token));
    }

    #endregion

    #region Helpers

    private static DisclosureGroup CreateDisclosureGroup(
        string groupId,
        string[] disclosedFields,
        Dictionary<string, object> filteredPayload,
        params RecipientInfo[] recipients)
    {
        return new DisclosureGroup
        {
            GroupId = groupId,
            DisclosedFields = disclosedFields,
            FilteredPayload = filteredPayload,
            Recipients = recipients
        };
    }

    private static RecipientInfo CreateRecipient(string walletAddress, WalletNetworks algorithm)
    {
        return new RecipientInfo
        {
            WalletAddress = walletAddress,
            PublicKey = new byte[] { 0x04, 0x01, 0x02, 0x03 },
            Algorithm = algorithm,
            Source = KeySource.Register
        };
    }

    #endregion
}
