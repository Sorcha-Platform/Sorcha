// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for transaction receipt endpoints (T026).
/// Covers GET receipt by txId, GET docket receipts with pagination,
/// POST batch receipts, and POST receipt verification.
/// </summary>
[Collection("RegisterWebApp")]
public class ReceiptEndpointTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _testRegisterId;

    public ReceiptEndpointTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("Receipt Test Register", "receipt-test-tenant").Result;
        _testRegisterId = register.Id;
    }

    // ===========================
    // GET /api/registers/{registerId}/transactions/{txId}/receipt
    // ===========================

    [Fact]
    public async Task GetTransactionReceipt_WithExistingReceipt_ShouldReturn200WithReceipt()
    {
        // Arrange — submit a transaction and store a receipt for it
        var txId = await SubmitTestTransactionAsync();
        var receipt = CreateTestReceipt(txId, _testRegisterId, docketNumber: 1);
        await InsertReceiptsDirectlyAsync(new[] { receipt });

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/receipt");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("transactionId").GetString().Should().Be(txId);
        result.GetProperty("registerId").GetString().Should().Be(_testRegisterId);
        result.GetProperty("receiptId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetTransactionReceipt_WithNonExistentTx_ShouldReturn404()
    {
        // Arrange
        var nonExistentTxId = new string('a', 64);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{nonExistentTxId}/receipt");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTransactionReceipt_WithNoReceipt_ShouldReturn404()
    {
        // Arrange — submit transaction but do NOT store a receipt
        var txId = await SubmitTestTransactionAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/receipt");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ===========================
    // GET /api/registers/{registerId}/dockets/{docketNumber}/receipts
    // ===========================

    [Fact]
    public async Task GetDocketReceipts_WithDefaultPagination_ShouldReturnPaginatedResults()
    {
        // Arrange — store multiple receipts for the same docket
        var docketNumber = 100L;
        var receipts = new List<TransactionReceipt>();
        for (int i = 0; i < 3; i++)
        {
            var txId = await SubmitTestTransactionAsync();
            receipts.Add(CreateTestReceipt(txId, _testRegisterId, docketNumber));
        }
        await InsertReceiptsDirectlyAsync(receipts);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/dockets/{docketNumber}/receipts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("page").GetInt32().Should().Be(1);
        result.GetProperty("pageSize").GetInt32().Should().Be(20);
        result.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetDocketReceipts_WithCustomPageSize_ShouldRespectPagination()
    {
        // Arrange
        var docketNumber = 200L;
        var receipts = new List<TransactionReceipt>();
        for (int i = 0; i < 3; i++)
        {
            var txId = await SubmitTestTransactionAsync();
            receipts.Add(CreateTestReceipt(txId, _testRegisterId, docketNumber));
        }
        await InsertReceiptsDirectlyAsync(receipts);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/dockets/{docketNumber}/receipts?page=1&pageSize=2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("page").GetInt32().Should().Be(1);
        result.GetProperty("pageSize").GetInt32().Should().Be(2);
        result.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }

    // ===========================
    // POST /api/registers/{registerId}/receipts/batch
    // ===========================

    [Fact]
    public async Task StoreBatchReceipts_WithValidReceipts_ShouldReturn201WithStoredCount()
    {
        // Arrange
        var txId1 = await SubmitTestTransactionAsync();
        var txId2 = await SubmitTestTransactionAsync();
        var docketNumber = 300L;

        var request = new
        {
            docketNumber,
            receipts = new[]
            {
                CreateTestReceipt(txId1, _testRegisterId, docketNumber),
                CreateTestReceipt(txId2, _testRegisterId, docketNumber)
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/receipts/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("stored").GetInt32().Should().Be(2);
        result.GetProperty("docketNumber").GetInt64().Should().Be(docketNumber);
    }

    [Fact]
    public async Task StoreBatchReceipts_WithEmptyArray_ShouldReturn400()
    {
        // Arrange
        var request = new
        {
            docketNumber = 1L,
            receipts = Array.Empty<TransactionReceipt>()
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/receipts/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StoreBatchReceipts_WithMismatchedRegisterId_ShouldReturn400()
    {
        // Arrange — receipt has a different registerId
        var txId = await SubmitTestTransactionAsync();
        var receipt = CreateTestReceipt(txId, "wrong-register-id", 1);
        var request = new
        {
            docketNumber = 1L,
            receipts = new[] { receipt }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/receipts/batch", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ===========================
    // POST /api/registers/{registerId}/receipts/verify
    // ===========================

    [Fact]
    public async Task VerifyReceipt_WithValidReceipt_ShouldReturnVerificationResult()
    {
        // Arrange
        var receipt = CreateTestReceipt("test-tx-001", _testRegisterId, 1);
        var request = new
        {
            receipt,
            validatorPublicKey = Convert.ToBase64String(new byte[32])
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/receipts/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.TryGetProperty("isValid", out _).Should().BeTrue();
        result.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.TryGetProperty("signatureValid", out _).Should().BeTrue();
        checks.TryGetProperty("inclusionProofValid", out _).Should().BeTrue();
        checks.TryGetProperty("merkleRootConsistent", out _).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyReceipt_WithNullReceipt_ShouldReturn400()
    {
        // Arrange
        var request = new
        {
            receipt = (TransactionReceipt?)null,
            validatorPublicKey = "somekey"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/receipts/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VerifyReceipt_WithEmptyPublicKey_ShouldReturn400()
    {
        // Arrange
        var receipt = CreateTestReceipt("test-tx-002", _testRegisterId, 1);
        var request = new
        {
            receipt,
            validatorPublicKey = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/receipts/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ===========================
    // Helper Methods
    // ===========================

    private async Task<string> SubmitTestTransactionAsync()
    {
        var txId = Guid.NewGuid().ToString("N") + new string('0', 64);
        txId = txId[..64];

        var transaction = new TransactionModel
        {
            RegisterId = _testRegisterId,
            TxId = txId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = "sender_wallet_address",
            RecipientsWallets = new[] { "recipient_wallet_address" },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { "sender_wallet_address" },
                    PayloadSize = 1024,
                    Hash = "payload_hash",
                    Data = "encrypted_payload_data"
                }
            },
            Signature = "transaction_signature"
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions", transaction);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel>();
        return result!.TxId;
    }

    private async Task InsertReceiptsDirectlyAsync(IEnumerable<TransactionReceipt> receipts)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();
        await repository.InsertReceiptsAsync(receipts);
    }

    private static TransactionReceipt CreateTestReceipt(
        string txId, string registerId, long docketNumber)
    {
        var merkleRoot = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(txId))).ToLowerInvariant();

        return new TransactionReceipt
        {
            ReceiptId = $"receipt-{Guid.NewGuid():N}",
            TransactionId = txId,
            RegisterId = registerId,
            DocketNumber = docketNumber,
            MerkleRoot = merkleRoot,
            InclusionProof = new MerkleInclusionProof
            {
                TransactionHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(txId))).ToLowerInvariant(),
                DocketNumber = docketNumber,
                MerkleRoot = merkleRoot,
                ProofPath = new List<MerkleProofStep>
                {
                    new MerkleProofStep
                    {
                        Hash = new string('b', 64),
                        Position = ProofPosition.Right
                    }
                }.AsReadOnly(),
                LeafIndex = 0,
                TreeSize = 2
            },
            Signatures = new List<ValidatorSignature>
            {
                new ValidatorSignature
                {
                    ValidatorAddress = "validator-001",
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            }.AsReadOnly(),
            SealedAt = DateTimeOffset.UtcNow
        };
    }
}
