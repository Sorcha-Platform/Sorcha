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
/// Tests for disclosure group formation and encryption via
/// <see cref="EncryptionPipelineService.EncryptDisclosedPayloadsAsync"/>.
/// Validates grouping by identical disclosure fields, wrapped key counts,
/// and atomic failure when key wrapping fails for any recipient.
/// </summary>
public class DisclosureGroupEncryptionTests
{
    private readonly Mock<ISymmetricCrypto> _symmetricCryptoMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly Mock<IHashProvider> _hashProviderMock;
    private readonly Mock<ILogger<EncryptionPipelineService>> _loggerMock;
    private readonly EncryptionPipelineService _sut;

    private static readonly byte[] FakeSymmetricKey = new byte[32];
    private static readonly byte[] FakeNonce = new byte[24];
    private static readonly byte[] FakeCiphertext = [0xDE, 0xAD, 0xBE, 0xEF];
    private static readonly byte[] FakePlaintextHash = new byte[32];
    private static readonly byte[] FakeWrappedKey = [0xCA, 0xFE, 0xBA, 0xBE];

    public DisclosureGroupEncryptionTests()
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

    #region Identical disclosure fields → 1 group, N wrapped keys

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_TwoRecipientsIdenticalFields_ReturnsOneGroupTwoWrappedKeys()
    {
        // Arrange — 2 recipients disclosed the same fields with the same values
        var builder = new DisclosureGroupBuilder();
        var sharedPayload = new Dictionary<string, object> { ["name"] = "SharedDoc", ["amount"] = 100 };

        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = sharedPayload,
            ["ws1qbob"] = sharedPayload
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice", WalletNetworks.ED25519),
            CreateRecipient("ws1qbob", WalletNetworks.NISTP256)
        };

        var groups = builder.BuildGroups(disclosedPayloads, recipients);

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — 1 group (identical fields + values), 2 wrapped keys
        result.Success.Should().BeTrue();
        result.Groups.Should().HaveCount(1);
        result.Groups[0].WrappedKeys.Should().HaveCount(2);

        var walletAddresses = result.Groups[0].WrappedKeys.Select(w => w.WalletAddress).ToArray();
        walletAddresses.Should().Contain("ws1qalice");
        walletAddresses.Should().Contain("ws1qbob");

        // Symmetric encryption once (one group), asymmetric wrapping twice (two recipients)
        _symmetricCryptoMock.Verify(s =>
            s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _cryptoModuleMock.Verify(c =>
            c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region Different disclosure fields → N groups, 1 wrapped key each

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_TwoRecipientsDifferentFields_ReturnsTwoGroupsOneWrappedKeyEach()
    {
        // Arrange — 2 recipients with different disclosed field sets
        var builder = new DisclosureGroupBuilder();

        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "Alice", ["amount"] = 100 },
            ["ws1qbob"] = new() { ["name"] = "Bob", ["ssn"] = "123-45-6789" }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice", WalletNetworks.ED25519),
            CreateRecipient("ws1qbob", WalletNetworks.NISTP256)
        };

        var groups = builder.BuildGroups(disclosedPayloads, recipients);

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — 2 groups (different fields or values), 1 wrapped key each
        result.Success.Should().BeTrue();
        result.Groups.Should().HaveCount(2);

        foreach (var group in result.Groups)
        {
            group.WrappedKeys.Should().HaveCount(1);
        }

        // Symmetric encryption twice (once per group), asymmetric wrapping twice (once per recipient)
        _symmetricCryptoMock.Verify(s =>
            s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _cryptoModuleMock.Verify(c =>
            c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region Key wrapping failure → atomic failure with FailedRecipient

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_KeyWrappingFailureForOneRecipient_FailsAtomicallyWithFailedRecipient()
    {
        // Arrange — key wrapping succeeds for Alice, fails for Bob
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
                if (callCount == 2) // Bob's key wrap
                {
                    return CryptoResult<byte[]>.Failure(
                        CryptoStatus.EncryptionFailed,
                        "Public key invalid for encryption");
                }
                return CryptoResult<byte[]>.Success(FakeWrappedKey);
            });

        // Two recipients in the same group (identical fields + values)
        var groups = new[]
        {
            CreateDisclosureGroup("group1", ["/name", "/amount"],
                new Dictionary<string, object> { ["name"] = "SharedDoc", ["amount"] = 100 },
                CreateRecipient("ws1qalice", WalletNetworks.ED25519),
                CreateRecipient("ws1qbob_fail", WalletNetworks.NISTP256))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — entire operation fails atomically
        result.Success.Should().BeFalse();
        result.FailedRecipient.Should().Be("ws1qbob_fail",
            because: "the specific wallet that caused the failure should be identified");
        result.Error.Should().NotBeNullOrEmpty();
        result.Groups.Should().BeEmpty(
            because: "atomic failure means no partial results");
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_KeyWrappingFailureAcrossGroups_FailsAtomicallyWithCorrectRecipient()
    {
        // Arrange — first group with Alice succeeds, second group with Bob fails
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
                if (wrapCallCount == 2) // Bob's key wrap (second group)
                {
                    return CryptoResult<byte[]>.Failure(
                        CryptoStatus.EncryptionFailed,
                        "Key wrap failed for second group");
                }
                return CryptoResult<byte[]>.Success(FakeWrappedKey);
            });

        var groups = new[]
        {
            CreateDisclosureGroup("group1", ["/name"],
                new Dictionary<string, object> { ["name"] = "Alice" },
                CreateRecipient("ws1qalice", WalletNetworks.ED25519)),
            CreateDisclosureGroup("group2", ["/ssn"],
                new Dictionary<string, object> { ["ssn"] = "123-45-6789" },
                CreateRecipient("ws1qbob_fail", WalletNetworks.NISTP256))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — entire operation fails, no partial results from first group
        result.Success.Should().BeFalse();
        result.FailedRecipient.Should().Be("ws1qbob_fail");
        result.Groups.Should().BeEmpty();
    }

    #endregion

    #region Mixed group sizes integration

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_ThreeGroupsMixedRecipients_CorrectWrappedKeyCounts()
    {
        // Arrange — 6 recipients across 3 groups:
        //   Group A: 3 recipients (identical fields + values)
        //   Group B: 2 recipients (identical fields + values)
        //   Group C: 1 recipient (unique fields)
        var builder = new DisclosureGroupBuilder();

        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1q1"] = new() { ["name"] = "SharedReport", ["amount"] = 100 },
            ["ws1q2"] = new() { ["name"] = "SharedReport", ["amount"] = 100 },
            ["ws1q3"] = new() { ["name"] = "SharedReport", ["amount"] = 100 },
            ["ws1q4"] = new() { ["name"] = "Confidential", ["ssn"] = "000-00-0000" },
            ["ws1q5"] = new() { ["name"] = "Confidential", ["ssn"] = "000-00-0000" },
            ["ws1q6"] = new() { ["date"] = "2026-01-01", ["ref"] = "ABC" }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1q1", WalletNetworks.ED25519),
            CreateRecipient("ws1q2", WalletNetworks.ED25519),
            CreateRecipient("ws1q3", WalletNetworks.ED25519),
            CreateRecipient("ws1q4", WalletNetworks.NISTP256),
            CreateRecipient("ws1q5", WalletNetworks.NISTP256),
            CreateRecipient("ws1q6", WalletNetworks.RSA4096)
        };

        var groups = builder.BuildGroups(disclosedPayloads, recipients);

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.Groups.Should().HaveCount(3);

        var wrappedKeyCounts = result.Groups
            .Select(g => g.WrappedKeys.Length)
            .OrderByDescending(c => c)
            .ToArray();
        wrappedKeyCounts.Should().BeEquivalentTo([3, 2, 1]);

        // Symmetric encryption 3 times (once per group), asymmetric 6 times (once per recipient)
        _symmetricCryptoMock.Verify(s =>
            s.EncryptAsync(
                It.IsAny<byte[]>(),
                EncryptionType.XCHACHA20_POLY1305,
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        _cryptoModuleMock.Verify(c =>
            c.EncryptAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(6));
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
