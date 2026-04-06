// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Register;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="FileReassemblyService"/>.
///
/// Encrypted-path scenarios that require a fully provisioned wallet (with persisted private key)
/// are covered by integration tests in Sorcha.Wallet.Service.IntegrationTests.
/// </summary>
public class FileReassemblyServiceTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock;
    private readonly Mock<ISymmetricCrypto> _symmetricCryptoMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly Mock<IHashProvider> _hashProviderMock;
    private readonly Mock<IWalletUtilities> _walletUtilitiesMock;
    private readonly InMemoryWalletRepository _walletRepository;
    private readonly LocalEncryptionProvider _encryptionProvider;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly WalletManager _walletManager;
    private readonly FileReassemblyService _sut;

    private const string WalletAddress = "ws1TestWalletAddress123";
    private const string RegisterId = "reg-0001";
    private const string ActionTxId = "aaaa0000000000000000000000000000000000000000000000000000000000001111";
    private const string ChunkTxId = "bbbb0000000000000000000000000000000000000000000000000000000000002222";
    private const string FieldName = "attachments";

    public FileReassemblyServiceTests()
    {
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _blueprintClientMock = new Mock<IBlueprintServiceClient>();
        _symmetricCryptoMock = new Mock<ISymmetricCrypto>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        _hashProviderMock = new Mock<IHashProvider>();
        _walletUtilitiesMock = new Mock<IWalletUtilities>();

        _walletRepository = new InMemoryWalletRepository();
        _encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        _eventPublisher = new InMemoryEventPublisher(Mock.Of<ILogger<InMemoryEventPublisher>>());

        var keyManagement = new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)_encryptionProvider,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            Mock.Of<ILogger<KeyManagementService>>());

        var transactionService = new TransactionService(
            _cryptoModuleMock.Object,
            _hashProviderMock.Object,
            Mock.Of<ILogger<TransactionService>>());

        var delegationService = new DelegationService(
            _walletRepository,
            Mock.Of<ILogger<DelegationService>>());

        _walletManager = new WalletManager(
            keyManagement,
            transactionService,
            delegationService,
            _walletRepository,
            _eventPublisher,
            Mock.Of<ILogger<WalletManager>>(),
            Mock.Of<IRecoveryKeyService>());

        _sut = new FileReassemblyService(
            _registerClientMock.Object,
            _blueprintClientMock.Object,
            _walletManager,
            _symmetricCryptoMock.Object,
            Mock.Of<ILogger<FileReassemblyService>>());
    }

    public void Dispose() { /* InMemoryWalletRepository has no Dispose */ }

    // -------------------------------------------------------------------------
    // Constructor guard tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_NullRegisterClient_ThrowsArgumentNullException()
    {
        var act = () => new FileReassemblyService(
            null!,
            _blueprintClientMock.Object,
            _walletManager,
            _symmetricCryptoMock.Object,
            Mock.Of<ILogger<FileReassemblyService>>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("registerClient");
    }

    [Fact]
    public void Constructor_NullBlueprintClient_ThrowsArgumentNullException()
    {
        var act = () => new FileReassemblyService(
            _registerClientMock.Object,
            null!,
            _walletManager,
            _symmetricCryptoMock.Object,
            Mock.Of<ILogger<FileReassemblyService>>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("blueprintClient");
    }

    [Fact]
    public void Constructor_NullWalletManager_ThrowsArgumentNullException()
    {
        var act = () => new FileReassemblyService(
            _registerClientMock.Object,
            _blueprintClientMock.Object,
            null!,
            _symmetricCryptoMock.Object,
            Mock.Of<ILogger<FileReassemblyService>>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("walletManager");
    }

    [Fact]
    public void Constructor_NullSymmetricCrypto_ThrowsArgumentNullException()
    {
        var act = () => new FileReassemblyService(
            _registerClientMock.Object,
            _blueprintClientMock.Object,
            _walletManager,
            null!,
            Mock.Of<ILogger<FileReassemblyService>>());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("symmetricCrypto");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new FileReassemblyService(
            _registerClientMock.Object,
            _blueprintClientMock.Object,
            _walletManager,
            _symmetricCryptoMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // -------------------------------------------------------------------------
    // PrepareDownloadAsync — argument validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null, RegisterId, ActionTxId, FieldName)]
    [InlineData("", RegisterId, ActionTxId, FieldName)]
    [InlineData("   ", RegisterId, ActionTxId, FieldName)]
    [InlineData(WalletAddress, null, ActionTxId, FieldName)]
    [InlineData(WalletAddress, "", ActionTxId, FieldName)]
    [InlineData(WalletAddress, RegisterId, null, FieldName)]
    [InlineData(WalletAddress, RegisterId, "", FieldName)]
    [InlineData(WalletAddress, RegisterId, ActionTxId, null)]
    [InlineData(WalletAddress, RegisterId, ActionTxId, "")]
    public async Task PrepareDownloadAsync_NullOrEmptyRequiredParams_ThrowsArgumentException(
        string walletAddress, string registerId, string actionTxId, string fieldName)
    {
        var act = async () => await _sut.PrepareDownloadAsync(
            walletAddress, registerId, actionTxId, fieldName, fileIndex: 0);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PrepareDownloadAsync_NegativeFileIndex_ThrowsArgumentOutOfRangeException()
    {
        var act = async () => await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: -1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("fileIndex");
    }

    // -------------------------------------------------------------------------
    // PrepareDownloadAsync — action transaction not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PrepareDownloadAsync_ActionTransactionNotFound_ReturnsNull()
    {
        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // PrepareDownloadAsync — missing payload data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PrepareDownloadAsync_TransactionHasNoPayloads_ReturnsNull()
    {
        var tx = BuildTransaction(ActionTxId, payloads: []);
        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_TransactionPayloadDataEmpty_ReturnsNull()
    {
        var tx = BuildTransaction(ActionTxId, payloads:
        [
            new PayloadModel { Data = "" }
        ]);
        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // PrepareDownloadAsync — plaintext (dev-mode) path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_FieldNotFound_ReturnsNull()
    {
        // Payload JSON contains a different field name — "documents" — not "attachments"
        var payloadObj = new { documents = BuildFileRefObject() };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_FileIndexOutOfRange_ReturnsNull()
    {
        var fileRefs = new[] { BuildFileRefObject() };
        var payloadObj = new { attachments = fileRefs };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);

        // Index 1 does not exist in a single-element array
        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_NoChunkTransactionIds_ReturnsNull()
    {
        var fileRef = BuildFileRefObject(chunkTxIds: []);
        var payloadObj = new { attachments = fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_SingleChunk_ReturnsResultWithCorrectMetadata()
    {
        // Arrange
        var fileContent = "Hello Sorcha!"u8.ToArray();
        var (fileRef, _) = BuildPlaintextChunkScenario(ChunkTxId, fileContent);

        var payloadObj = new { attachments = fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(ChunkTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(ChunkTxId, fileContent, "application/octet-stream"));

        // Act
        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be(fileRef.FileName);
        result.ContentType.Should().Be(fileRef.ContentType);
        result.Size.Should().Be(fileRef.Size);
        result.WriteToStreamAsync.Should().NotBeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_SingleChunk_WritesCorrectBytesToStream()
    {
        // Arrange
        var fileContent = "Hello Sorcha!"u8.ToArray();
        var (fileRef, _) = BuildPlaintextChunkScenario(ChunkTxId, fileContent);

        var payloadObj = new { attachments = fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(ChunkTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(ChunkTxId, fileContent, "application/octet-stream"));

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        // Act
        using var ms = new MemoryStream();
        await result!.WriteToStreamAsync(ms, CancellationToken.None);

        // Assert
        ms.ToArray().Should().BeEquivalentTo(fileContent);
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_MultipleChunks_ReassemblesInOrder()
    {
        // Arrange
        var chunk0 = "First chunk"u8.ToArray();
        var chunk1 = " Second chunk"u8.ToArray();
        var chunk2 = " Third chunk"u8.ToArray();
        var expectedContent = chunk0.Concat(chunk1).Concat(chunk2).ToArray();

        var chunkTxId0 = "cc" + new string('0', 62);
        var chunkTxId1 = "dd" + new string('0', 62);
        var chunkTxId2 = "ee" + new string('0', 62);

        var fileHash = ComputeSha256Hash(expectedContent);
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var fileRef = new
        {
            fileName = "multipart.txt",
            contentType = "text/plain",
            size = (long)expectedContent.Length,
            hash = fileHash,
            salt,
            chunkTransactionIds = new[] { chunkTxId0, chunkTxId1, chunkTxId2 },
            masterKeyId = ActionTxId
        };

        var payloadObj = new { attachments = fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(chunkTxId0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(chunkTxId0, chunk0, "text/plain"));
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(chunkTxId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(chunkTxId1, chunk1, "text/plain"));
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(chunkTxId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(chunkTxId2, chunk2, "text/plain"));

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        // Act
        using var ms = new MemoryStream();
        await result!.WriteToStreamAsync(ms, CancellationToken.None);

        // Assert
        ms.ToArray().Should().BeEquivalentTo(expectedContent);
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_ChunkTransactionNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var fileContent = "Missing chunk"u8.ToArray();
        var (_, _) = BuildPlaintextChunkScenario(ChunkTxId, fileContent);

        var fileRef = BuildFileRefObject(chunkTxIds: [ChunkTxId], content: fileContent);
        var payloadObj = new { attachments = fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(ChunkTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileChunkRetrievalResult?)null);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);
        result.Should().NotBeNull();

        // Act
        using var ms = new MemoryStream();
        var act = async () => await result!.WriteToStreamAsync(ms, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{ChunkTxId}*");
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_HashMismatch_ThrowsInvalidOperationException()
    {
        // Arrange: correct chunk data but deliberately wrong hash stored in FileReference
        var fileContent = "Original content"u8.ToArray();
        var tamperedHash = "sha256:" + new string('a', 64); // plausible but wrong
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var fileRef = new
        {
            fileName = "tampered.txt",
            contentType = "text/plain",
            size = (long)fileContent.Length,
            hash = tamperedHash,
            salt,
            chunkTransactionIds = new[] { ChunkTxId },
            masterKeyId = ActionTxId
        };

        var payloadObj = new { attachments = fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(ChunkTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(ChunkTxId, fileContent, "application/octet-stream"));

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);
        result.Should().NotBeNull();

        // Act
        using var ms = new MemoryStream();
        var act = async () => await result!.WriteToStreamAsync(ms, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*integrity check failed*");
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_SingleFileAsObject_NotArray_ReturnsResult()
    {
        // When the field is a single FileReference object (not an array), fileIndex 0 should succeed
        var fileContent = "Single object"u8.ToArray();
        var (fileRef, _) = BuildPlaintextChunkScenario(ChunkTxId, fileContent);

        // Use the object directly (not wrapped in an array)
        var payloadObj = new { attachments = (object)fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(ChunkTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(ChunkTxId, fileContent, "application/octet-stream"));

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().NotBeNull();
        result!.FileName.Should().Be(fileRef.FileName);
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_SingleFileAsObject_FileIndexNonZero_ReturnsNull()
    {
        // When the field is a single object, any fileIndex != 0 should return null
        var fileContent = "Single object"u8.ToArray();
        var (fileRef, _) = BuildPlaintextChunkScenario(ChunkTxId, fileContent);

        var payloadObj = new { attachments = (object)fileRef };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadObj);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_PlaintextPath_FieldLookupIsCaseInsensitive()
    {
        // Payload stores the field as "Attachments" (capital A).
        // PrepareDownloadAsync is called with fieldName="attachments" (lower).
        // The service iterates payload properties case-insensitively, so it should match.
        var fileContent = "Case insensitive"u8.ToArray();
        var (fileRef, _) = BuildPlaintextChunkScenario(ChunkTxId, fileContent);

        // Build payload object with uppercase key (anonymous type serialises as-is)
        var payloadWithUppercaseKey = new Dictionary<string, object>
        {
            ["Attachments"] = fileRef   // stored with capital A in the JSON
        };
        var actionTx = BuildPlaintextActionTransaction(ActionTxId, payloadWithUppercaseKey);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);
        _blueprintClientMock
            .Setup(c => c.GetFileChunkAsync(ChunkTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileChunkRetrievalResult(ChunkTxId, fileContent, "application/octet-stream"));

        // fieldName is lowercase "attachments" — service should still find "Attachments"
        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, "attachments", fileIndex: 0);

        result.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // PrepareDownloadAsync — encrypted path (wallet not in wrappedKeys)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PrepareDownloadAsync_EncryptedPath_WalletNotAuthorised_ReturnsNull()
    {
        // Build a transaction with contentEncoding=encrypted and a wrappedKeys array
        // that does NOT include our wallet address
        var encryptedActionJson = BuildEncryptedActionJson(
            authorisedWalletAddress: "ws1SomeOtherWallet");

        var actionTx = BuildTransactionFromPayloadJson(ActionTxId, encryptedActionJson);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PrepareDownloadAsync_EncryptedPath_NoEncryptedPayloadsArray_ReturnsNull()
    {
        // contentEncoding=encrypted but no encryptedPayloads array at all
        var json = JsonSerializer.Serialize(new
        {
            contentEncoding = "encrypted"
            // no encryptedPayloads key
        });
        var actionTx = BuildTransactionFromPayloadJson(ActionTxId, json);

        _registerClientMock
            .Setup(c => c.GetTransactionAsync(RegisterId, ActionTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionTx);

        var result = await _sut.PrepareDownloadAsync(
            WalletAddress, RegisterId, ActionTxId, FieldName, fileIndex: 0);

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // FileDownloadResult record — structural tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FileDownloadResult_Constructor_SetsAllProperties()
    {
        Func<Stream, CancellationToken, Task> callback = (_, _) => Task.CompletedTask;

        var record = new FileDownloadResult("report.pdf", "application/pdf", 4096, callback);

        record.FileName.Should().Be("report.pdf");
        record.ContentType.Should().Be("application/pdf");
        record.Size.Should().Be(4096);
        record.WriteToStreamAsync.Should().BeSameAs(callback);
    }

    [Fact]
    public void FileDownloadResult_EqualityIsValueBased()
    {
        Func<Stream, CancellationToken, Task> callback = (_, _) => Task.CompletedTask;
        var a = new FileDownloadResult("file.txt", "text/plain", 100, callback);
        var b = new FileDownloadResult("file.txt", "text/plain", 100, callback);

        a.Should().Be(b);
    }

    // -------------------------------------------------------------------------
    // HkdfKeyDerivation — static helper (covered here for completeness)
    // -------------------------------------------------------------------------

    [Fact]
    public void HkdfKeyDerivation_DeriveChunkKey_ProducesCorrectLength()
    {
        var masterKey = RandomNumberGenerator.GetBytes(HkdfKeyDerivation.KeySizeBytes);
        var salt = RandomNumberGenerator.GetBytes(32);

        var derived = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 0);

        derived.Should().HaveCount(HkdfKeyDerivation.KeySizeBytes);
    }

    [Fact]
    public void HkdfKeyDerivation_DeriveChunkKey_DifferentIndicesProduceDifferentKeys()
    {
        var masterKey = RandomNumberGenerator.GetBytes(HkdfKeyDerivation.KeySizeBytes);
        var salt = RandomNumberGenerator.GetBytes(32);

        var key0 = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 0);
        var key1 = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 1);

        key0.Should().NotBeEquivalentTo(key1);
    }

    [Fact]
    public void HkdfKeyDerivation_DeriveChunkKey_IsDeterministic()
    {
        var masterKey = RandomNumberGenerator.GetBytes(HkdfKeyDerivation.KeySizeBytes);
        var salt = RandomNumberGenerator.GetBytes(32);

        var first = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 2);
        var second = HkdfKeyDerivation.DeriveChunkKey(masterKey, salt, chunkIndex: 2);

        first.Should().BeEquivalentTo(second);
    }

    [Fact]
    public void HkdfKeyDerivation_DeriveChunkKey_WrongMasterKeySize_ThrowsArgumentException()
    {
        var badKey = new byte[16]; // must be 32
        var salt = new byte[32];

        var act = () => HkdfKeyDerivation.DeriveChunkKey(badKey, salt, chunkIndex: 0);

        act.Should().Throw<ArgumentException>().WithParameterName("masterFileKey");
    }

    [Fact]
    public void HkdfKeyDerivation_DeriveChunkKey_NegativeIndex_ThrowsArgumentException()
    {
        var key = RandomNumberGenerator.GetBytes(HkdfKeyDerivation.KeySizeBytes);
        var salt = new byte[32];

        var act = () => HkdfKeyDerivation.DeriveChunkKey(key, salt, chunkIndex: -1);

        act.Should().Throw<ArgumentException>().WithParameterName("chunkIndex");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Builds a minimal TransactionModel with given payloads.</summary>
    private static TransactionModel BuildTransaction(string txId, PayloadModel[] payloads) =>
        new()
        {
            TxId = txId,
            RegisterId = RegisterId,
            SenderWallet = WalletAddress,
            Signature = "sig",
            Payloads = payloads
        };

    /// <summary>
    /// Builds a plaintext action transaction whose Payloads[0].Data is a base64-encoded JSON
    /// of <paramref name="payloadObject"/>, without contentEncoding=encrypted.
    /// </summary>
    private static TransactionModel BuildPlaintextActionTransaction(string txId, object payloadObject)
    {
        var payloadJson = JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Wrap in the outer tx JSON structure expected by the parser (payloads key)
        var txBodyJson = JsonSerializer.Serialize(new
        {
            payloads = payloadObject
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(txBodyJson));
        return BuildTransaction(txId, [new PayloadModel { Data = data }]);
    }

    /// <summary>
    /// Builds an action transaction from an already-serialised payload JSON string.
    /// </summary>
    private static TransactionModel BuildTransactionFromPayloadJson(string txId, string payloadJson)
    {
        var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
        return BuildTransaction(txId, [new PayloadModel { Data = data }]);
    }

    /// <summary>
    /// Builds a chunk transaction whose Payloads[0].Data contains the raw plaintext bytes
    /// base64-encoded (dev-mode / plaintext fallback path).
    /// </summary>
    private static TransactionModel BuildRawChunkTransaction(string txId, byte[] chunkBytes)
    {
        var data = Convert.ToBase64String(chunkBytes);
        return BuildTransaction(txId, [new PayloadModel { Data = data }]);
    }

    /// <summary>
    /// Creates a pair of (FileReference DTO, chunk TransactionModel) for plaintext tests.
    /// The SHA-256 hash in the DTO matches the provided <paramref name="content"/>.
    /// Returns a <see cref="FileReferenceDto"/> record so callers can access typed properties.
    /// </summary>
    private static (FileReferenceDto FileRef, TransactionModel ChunkTx) BuildPlaintextChunkScenario(
        string chunkTxId, byte[] content)
    {
        var hash = ComputeSha256Hash(content);
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var fileRef = new FileReferenceDto(
            FileName: "test-file.bin",
            ContentType: "application/octet-stream",
            Size: content.Length,
            Hash: hash,
            Salt: salt,
            ChunkTransactionIds: [chunkTxId],
            MasterKeyId: ActionTxId);

        var chunkTx = BuildRawChunkTransaction(chunkTxId, content);
        return (fileRef, chunkTx);
    }

    /// <summary>
    /// Strongly-typed DTO mirroring <c>FileReference</c> JSON structure.
    /// Uses camelCase property names so <see cref="JsonSerializer"/> serialises correctly.
    /// </summary>
    private sealed record FileReferenceDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("fileName")]   string FileName,
        [property: System.Text.Json.Serialization.JsonPropertyName("contentType")] string ContentType,
        [property: System.Text.Json.Serialization.JsonPropertyName("size")]        long Size,
        [property: System.Text.Json.Serialization.JsonPropertyName("hash")]        string Hash,
        [property: System.Text.Json.Serialization.JsonPropertyName("salt")]        string Salt,
        [property: System.Text.Json.Serialization.JsonPropertyName("chunkTransactionIds")] string[] ChunkTransactionIds,
        [property: System.Text.Json.Serialization.JsonPropertyName("masterKeyId")] string MasterKeyId);

    /// <summary>Builds a FileReference anonymous object with optional overrides.</summary>
    private static dynamic BuildFileRefObject(
        string[]? chunkTxIds = null,
        byte[]? content = null)
    {
        var bytes = content ?? "placeholder"u8.ToArray();
        return new
        {
            fileName = "test.bin",
            contentType = "application/octet-stream",
            size = (long)bytes.Length,
            hash = ComputeSha256Hash(bytes),
            salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            chunkTransactionIds = chunkTxIds ?? [ChunkTxId],
            masterKeyId = ActionTxId
        };
    }

    /// <summary>
    /// Builds JSON for an encrypted action transaction with a wrappedKeys entry for the given wallet.
    /// </summary>
    private static string BuildEncryptedActionJson(string authorisedWalletAddress)
    {
        var envelope = new
        {
            contentEncoding = "encrypted",
            encryptedPayloads = new[]
            {
                new
                {
                    ciphertext = Convert.ToBase64String(new byte[32]),
                    nonce = Convert.ToBase64String(new byte[24]),
                    wrappedKeys = new[]
                    {
                        new
                        {
                            walletAddress = authorisedWalletAddress,
                            encryptedKey = Convert.ToBase64String(new byte[64])
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>Computes SHA-256 of bytes and returns a "sha256:{hex}" string.</summary>
    private static string ComputeSha256Hash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
