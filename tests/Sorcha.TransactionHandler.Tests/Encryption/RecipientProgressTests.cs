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
/// Tests that <see cref="EncryptionPipelineService.EncryptDisclosedPayloadsAsync"/>
/// populates per-recipient progress entries on the result.
/// </summary>
public class RecipientProgressTests
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

    public RecipientProgressTests()
    {
        _symmetricCryptoMock = new Mock<ISymmetricCrypto>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        _hashProviderMock = new Mock<IHashProvider>();
        _loggerMock = new Mock<ILogger<EncryptionPipelineService>>();

        _hashProviderMock
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(FakePlaintextHash);

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

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_ThreeRecipientsAcrossTwoGroups_ReturnsThreeRecipientProgressEntries()
    {
        // Arrange — 3 recipients across 2 groups
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name", "/age"],
                new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30 },
                CreateRecipient("ws1qalice", WalletNetworks.ED25519, "Alice"),
                CreateRecipient("ws1qbob", WalletNetworks.NISTP256, "Bob")),
            CreateDisclosureGroup("g2", ["/ssn"],
                new Dictionary<string, object> { ["ssn"] = "123-45-6789" },
                CreateRecipient("ws1qcarol", WalletNetworks.RSA4096, "Carol"))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.RecipientProgressEntries.Should().HaveCount(3);
        result.RecipientProgressEntries.Should().AllSatisfy(rp =>
            rp.Status.Should().Be(RecipientProgressStatus.Secured));
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_RecipientProgressEntries_HaveCorrectFields()
    {
        // Arrange
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/name", "/email"],
                new Dictionary<string, object> { ["name"] = "Test", ["email"] = "test@test.com" },
                CreateRecipient("ws1qalice", WalletNetworks.ED25519, "Alice Johnson")),
            CreateDisclosureGroup("g2", ["/ssn"],
                new Dictionary<string, object> { ["ssn"] = "555-12-3456" },
                CreateRecipient("ws1qbob", WalletNetworks.NISTP256, "Bob Smith"),
                CreateRecipient("ws1qcarol", WalletNetworks.RSA4096, "Carol White"))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert — verify each entry has correct wallet, display name, fields, group
        result.Success.Should().BeTrue();
        result.RecipientProgressEntries.Should().HaveCount(3);

        var alice = result.RecipientProgressEntries.Single(rp => rp.WalletAddress == "ws1qalice");
        alice.DisplayName.Should().Be("Alice Johnson");
        alice.DisclosedFields.Should().BeEquivalentTo(["/name", "/email"]);
        alice.GroupId.Should().Be("g1");
        alice.Status.Should().Be(RecipientProgressStatus.Secured);

        var bob = result.RecipientProgressEntries.Single(rp => rp.WalletAddress == "ws1qbob");
        bob.DisplayName.Should().Be("Bob Smith");
        bob.DisclosedFields.Should().BeEquivalentTo(["/ssn"]);
        bob.GroupId.Should().Be("g2");
        bob.Status.Should().Be(RecipientProgressStatus.Secured);

        var carol = result.RecipientProgressEntries.Single(rp => rp.WalletAddress == "ws1qcarol");
        carol.DisplayName.Should().Be("Carol White");
        carol.DisclosedFields.Should().BeEquivalentTo(["/ssn"]);
        carol.GroupId.Should().Be("g2");
        carol.Status.Should().Be(RecipientProgressStatus.Secured);
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_KeyWrappingFails_FailedRecipientHasStatusFailedWithError()
    {
        // Arrange — second recipient fails
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
                        "Invalid public key format");
                }
                return CryptoResult<byte[]>.Success(FakeWrappedKey);
            });

        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "secret" },
                CreateRecipient("ws1qok", WalletNetworks.ED25519, "Good Recipient"),
                CreateRecipient("ws1qfail", WalletNetworks.NISTP256, "Bad Recipient"),
                CreateRecipient("ws1qskipped", WalletNetworks.RSA4096, "Skipped Recipient"))
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeFalse();
        result.FailedRecipient.Should().Be("ws1qfail");

        // Should have 2 entries: first succeeded, second failed, third never processed
        result.RecipientProgressEntries.Should().HaveCount(2);

        var ok = result.RecipientProgressEntries.First(rp => rp.WalletAddress == "ws1qok");
        ok.Status.Should().Be(RecipientProgressStatus.Secured);
        ok.ErrorMessage.Should().BeNull();

        var failed = result.RecipientProgressEntries.First(rp => rp.WalletAddress == "ws1qfail");
        failed.Status.Should().Be(RecipientProgressStatus.Failed);
        failed.ErrorMessage.Should().Contain("Invalid public key format");
        failed.DisplayName.Should().Be("Bad Recipient");
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_EmptyGroups_ReturnsEmptyRecipientProgress()
    {
        // Arrange
        var groups = Array.Empty<DisclosureGroup>();

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.RecipientProgressEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task EncryptDisclosedPayloadsAsync_RecipientWithNullDisplayName_StillTracked()
    {
        // Arrange — no display name set
        var groups = new[]
        {
            CreateDisclosureGroup("g1", ["/data"],
                new Dictionary<string, object> { ["data"] = "value" },
                new RecipientInfo
                {
                    WalletAddress = "ws1qnodisplay",
                    PublicKey = new byte[] { 0x04, 0x01, 0x02, 0x03 },
                    Algorithm = WalletNetworks.ED25519,
                    Source = KeySource.Register,
                    DisplayName = null
                })
        };

        // Act
        var result = await _sut.EncryptDisclosedPayloadsAsync(groups);

        // Assert
        result.Success.Should().BeTrue();
        result.RecipientProgressEntries.Should().HaveCount(1);
        result.RecipientProgressEntries[0].WalletAddress.Should().Be("ws1qnodisplay");
        result.RecipientProgressEntries[0].DisplayName.Should().BeNull();
        result.RecipientProgressEntries[0].Status.Should().Be(RecipientProgressStatus.Secured);
    }

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

    private static RecipientInfo CreateRecipient(string walletAddress, WalletNetworks algorithm, string? displayName = null)
    {
        return new RecipientInfo
        {
            WalletAddress = walletAddress,
            PublicKey = new byte[] { 0x04, 0x01, 0x02, 0x03 },
            Algorithm = algorithm,
            Source = KeySource.Register,
            DisplayName = displayName
        };
    }

    #endregion
}
