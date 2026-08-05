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
/// Integration tests for Merkle inclusion proof endpoints (T031).
/// Covers GET inclusion proof generation and POST proof verification.
/// </summary>
[Collection("RegisterWebApp")]
public class InclusionProofEndpointTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _testRegisterId;

    public InclusionProofEndpointTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("Proof Test Register", "proof-test-tenant").Result;
        _testRegisterId = register.Id;
    }

    // ===========================
    // GET /api/registers/{registerId}/transactions/{txId}/inclusion-proof
    // ===========================

    [Fact]
    public async Task GetInclusionProof_WithSealedTransaction_ShouldReturn200WithProof()
    {
        // Arrange — submit a transaction, create a docket that contains it, then query
        var txId = await SubmitTestTransactionAsync();
        var docket = await CreateDocketWithTransactionAsync(txId);

        // Assign the transaction to the docket
        await AssignTransactionToDocketAsync(txId, docket.Id);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/inclusion-proof");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.TryGetProperty("transactionHash", out _).Should().BeTrue();
        result.TryGetProperty("merkleRoot", out _).Should().BeTrue();
        result.TryGetProperty("proofPath", out _).Should().BeTrue();
        result.TryGetProperty("leafIndex", out _).Should().BeTrue();
        result.TryGetProperty("treeSize", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetInclusionProof_WithNonExistentTransaction_ShouldReturn404()
    {
        // Arrange
        var nonExistentTxId = new string('f', 64);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{nonExistentTxId}/inclusion-proof");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInclusionProof_WithUnsealedTransaction_ShouldReturn409Conflict()
    {
        // Arrange — submit a transaction but do NOT create a docket for it
        var txId = await SubmitTestTransactionAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/inclusion-proof");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ===========================
    // POST /api/registers/{registerId}/inclusion-proofs/verify
    // ===========================

    [Fact]
    public async Task VerifyInclusionProof_WithMatchingRoot_ShouldReturnValid()
    {
        // Arrange — build a valid proof by computing hashes
        // leaf = SHA256("tx-hash-a"), sibling = SHA256("tx-hash-b")
        // root = SHA256(leaf + sibling) when sibling is Right
        var txHash = ComputeSha256Hex("tx-hash-a");
        var siblingHash = ComputeSha256Hex("tx-hash-b");

        // Compute expected root: hash(txHash + siblingHash) since sibling is Right
        var combined = txHash + siblingHash;
        var expectedRoot = ComputeSha256Hex(combined);

        var request = new
        {
            transactionHash = txHash,
            merkleRoot = expectedRoot,
            proofPath = new[]
            {
                new { hash = siblingHash, position = 1 } // Right = 1
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/inclusion-proofs/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeTrue();
        result.GetProperty("computedRoot").GetString().Should().Be(expectedRoot);
    }

    [Fact]
    public async Task VerifyInclusionProof_WithTamperedRoot_ShouldReturnInvalid()
    {
        // Arrange
        var txHash = ComputeSha256Hex("tx-hash-tamper");
        var siblingHash = ComputeSha256Hex("sibling-tamper");
        var tamperedRoot = new string('0', 64);

        var request = new
        {
            transactionHash = txHash,
            merkleRoot = tamperedRoot,
            proofPath = new[]
            {
                new { hash = siblingHash, position = 1 }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/inclusion-proofs/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task VerifyInclusionProof_WithEmptyTransactionHash_ShouldReturn400()
    {
        // Arrange
        var request = new
        {
            transactionHash = "",
            merkleRoot = new string('a', 64),
            proofPath = new[]
            {
                new { hash = new string('b', 64), position = 1 }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/inclusion-proofs/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VerifyInclusionProof_WithEmptyProofPath_ShouldReturn400()
    {
        // Arrange
        var request = new
        {
            transactionHash = new string('a', 64),
            merkleRoot = new string('b', 64),
            proofPath = Array.Empty<object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/inclusion-proofs/verify", request);

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
            SenderWallet = "sender_wallet",
            RecipientsWallets = new[] { "recipient_wallet" },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { "sender_wallet" },
                    PayloadSize = 512,
                    Hash = "payload_hash",
                    Data = "test_data"
                }
            },
            Signature = "test_signature"
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions", transaction);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel>();
        return result!.TxId;
    }

    private async Task<DocketHeader> CreateDocketWithTransactionAsync(string txId)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();

        var docket = new DocketHeader
        {
            RegisterId = _testRegisterId,
            TransactionIds = new List<string> { txId },
            Hash = ComputeSha256Hex(txId),
            PreviousHash = string.Empty,
            TimeStamp = DateTime.UtcNow
        };

        return await repository.InsertDocketAsync(docket);
    }

    private async Task AssignTransactionToDocketAsync(string txId, ulong docketId)
    {
        // The in-memory repository stores transactions; we need to update the
        // DocketNumber on the transaction. We do this by re-inserting with the docket set.
        // Since in-memory storage may not support direct updates, we rely on the
        // docket's TransactionIds list for the inclusion proof endpoint.
        // The endpoint checks transaction.DocketNumber, so we need to set it.
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();

        // Get the existing transaction and delete + re-insert with DocketNumber set
        var tx = await repository.GetTransactionAsync(_testRegisterId, txId);
        if (tx is not null)
        {
            await repository.DeleteTransactionAsync(_testRegisterId, txId);
            var updated = new TransactionModel
            {
                RegisterId = tx.RegisterId,
                TxId = tx.TxId,
                PrevTxId = tx.PrevTxId,
                Version = tx.Version,
                SenderWallet = tx.SenderWallet,
                RecipientsWallets = tx.RecipientsWallets,
                TimeStamp = tx.TimeStamp,
                PayloadCount = tx.PayloadCount,
                Payloads = tx.Payloads,
                Signature = tx.Signature,
                MetaData = tx.MetaData,
                DocketNumber = docketId
            };
            await repository.InsertTransactionAsync(updated);
        }
    }

    private static string ComputeSha256Hex(string input)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
