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
/// Integration tests for verification bundle endpoints (T044).
/// Covers GET bundle export and POST bundle verification.
/// </summary>
[Collection("RegisterWebApp")]
public class VerificationBundleTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _testRegisterId;

    public VerificationBundleTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("Bundle Test Register", "bundle-test-tenant").Result;
        _testRegisterId = register.Id;
    }

    // ===========================
    // GET /api/registers/{registerId}/transactions/{txId}/verification-bundle
    // ===========================

    [Fact]
    public async Task GetVerificationBundle_WithSealedTransaction_ShouldReturn200WithBundle()
    {
        // Arrange — submit transaction and store a receipt for it
        var txId = await SubmitTestTransactionAsync();
        var receipt = CreateTestReceipt(txId, _testRegisterId, docketNumber: 1);
        await InsertReceiptsDirectlyAsync(new[] { receipt });

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/verification-bundle");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("version").GetInt32().Should().Be(1);
        result.GetProperty("transactionId").GetString().Should().Be(txId);
        result.GetProperty("registerId").GetString().Should().Be(_testRegisterId);
        result.TryGetProperty("credential", out _).Should().BeTrue();
        result.TryGetProperty("receipt", out _).Should().BeTrue();
        result.TryGetProperty("revocationStatus", out _).Should().BeTrue();
        result.TryGetProperty("exportedAt", out _).Should().BeTrue();
        result.TryGetProperty("validatorPublicKeys", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetVerificationBundle_WithNonExistentTransaction_ShouldReturn404()
    {
        // Arrange
        var nonExistentTxId = new string('d', 64);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{nonExistentTxId}/verification-bundle");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetVerificationBundle_WithUnsealedTransaction_ShouldReturn409Conflict()
    {
        // Arrange — submit transaction but do NOT store a receipt
        var txId = await SubmitTestTransactionAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/verification-bundle");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("error").GetString().Should().Contain("not been sealed");
    }

    [Fact]
    public async Task GetVerificationBundle_WithActiveTransaction_ShouldShowActiveRevocationStatus()
    {
        // Arrange
        var txId = await SubmitTestTransactionAsync();
        var receipt = CreateTestReceipt(txId, _testRegisterId, docketNumber: 2);
        await InsertReceiptsDirectlyAsync(new[] { receipt });

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/verification-bundle");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var revocationStatus = result.GetProperty("revocationStatus");
        revocationStatus.GetProperty("transactionId").GetString().Should().Be(txId);
        revocationStatus.GetProperty("status").GetInt32().Should().Be((int)TransactionLifecycleStatus.Active);
    }

    [Fact]
    public async Task GetVerificationBundle_WithRevokedTransaction_ShouldShowRevokedStatus()
    {
        // Arrange — submit, seal, then revoke
        var txId = await SubmitTestTransactionAsync();
        var receipt = CreateTestReceipt(txId, _testRegisterId, docketNumber: 3);
        await InsertReceiptsDirectlyAsync(new[] { receipt });

        // Revoke the transaction
        var revokeRequest = new
        {
            originalTxId = txId,
            reason = "Withdrawn"
        };
        var revokeResponse = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/verification-bundle");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var revocationStatus = result.GetProperty("revocationStatus");
        revocationStatus.GetProperty("status").GetInt32().Should().Be((int)TransactionLifecycleStatus.Revoked);
        revocationStatus.GetProperty("revocationTxId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetVerificationBundle_ShouldIncludeValidatorPublicKeys()
    {
        // Arrange
        var txId = await SubmitTestTransactionAsync();
        var receipt = CreateTestReceipt(txId, _testRegisterId, docketNumber: 4);
        await InsertReceiptsDirectlyAsync(new[] { receipt });

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/verification-bundle");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = result.GetProperty("validatorPublicKeys");
        keys.GetArrayLength().Should().BeGreaterThan(0);
        var firstKey = keys[0];
        firstKey.GetProperty("address").GetString().Should().Be("validator-001");
        firstKey.GetProperty("algorithm").GetString().Should().Be("ED25519");
    }

    // ===========================
    // POST /api/registers/{registerId}/verification-bundles/verify
    // ===========================

    [Fact]
    public async Task VerifyBundle_WithValidBundle_ShouldReturnVerificationResult()
    {
        // Arrange — build a minimal bundle for verification
        var txId = "test-tx-bundle-verify";
        var merkleRoot = ComputeSha256Hex(txId);

        var bundle = new
        {
            version = 1,
            transactionId = txId,
            registerId = _testRegisterId,
            credential = new[] { new { data = "test-credential" } },
            receipt = new
            {
                receiptId = "receipt-verify-001",
                transactionId = txId,
                registerId = _testRegisterId,
                docketNumber = 1L,
                merkleRoot,
                inclusionProof = new
                {
                    transactionHash = merkleRoot,
                    docketNumber = 1L,
                    merkleRoot,
                    proofPath = Array.Empty<object>(),
                    leafIndex = 0,
                    treeSize = 1
                },
                signatures = new[]
                {
                    new
                    {
                        validatorAddress = "validator-001",
                        signatureValue = Convert.ToBase64String(new byte[64]),
                        algorithm = "ED25519",
                        signedAt = DateTimeOffset.UtcNow
                    }
                },
                sealedAt = DateTimeOffset.UtcNow,
                version = 1
            },
            revocationStatus = new
            {
                transactionId = txId,
                status = 0 // Active
            },
            exportedAt = DateTimeOffset.UtcNow,
            validatorPublicKeys = new[]
            {
                new
                {
                    address = "validator-001",
                    publicKey = Convert.ToBase64String(new byte[32]),
                    algorithm = "ED25519"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/verification-bundles/verify", bundle);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.TryGetProperty("isValid", out _).Should().BeTrue();
        result.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.TryGetProperty("credentialSignatureValid", out _).Should().BeTrue();
        checks.TryGetProperty("inclusionProofValid", out _).Should().BeTrue();
        checks.TryGetProperty("receiptSignatureValid", out _).Should().BeTrue();
        checks.TryGetProperty("revocationStatusCurrent", out _).Should().BeTrue();
    }

    [Fact]
    public async Task VerifyBundle_WithPartialFailures_ShouldReturnErrorsAndWarnings()
    {
        // Arrange — bundle with tampered Merkle root so inclusion proof fails
        var txId = "test-tx-bundle-partial";
        var realRoot = ComputeSha256Hex(txId);
        var tamperedRoot = new string('0', 64);

        var bundle = new
        {
            version = 1,
            transactionId = txId,
            registerId = _testRegisterId,
            credential = new[] { new { data = "test-credential" } },
            receipt = new
            {
                receiptId = "receipt-partial-001",
                transactionId = txId,
                registerId = _testRegisterId,
                docketNumber = 1L,
                merkleRoot = tamperedRoot, // Tampered
                inclusionProof = new
                {
                    transactionHash = realRoot,
                    docketNumber = 1L,
                    merkleRoot = realRoot, // Does not match receipt merkleRoot
                    proofPath = Array.Empty<object>(),
                    leafIndex = 0,
                    treeSize = 1
                },
                signatures = new[]
                {
                    new
                    {
                        validatorAddress = "validator-001",
                        signatureValue = Convert.ToBase64String(new byte[64]),
                        algorithm = "ED25519",
                        signedAt = DateTimeOffset.UtcNow
                    }
                },
                sealedAt = DateTimeOffset.UtcNow,
                version = 1
            },
            revocationStatus = new
            {
                transactionId = txId,
                status = 0
            },
            exportedAt = DateTimeOffset.UtcNow,
            validatorPublicKeys = new[]
            {
                new
                {
                    address = "validator-001",
                    publicKey = Convert.ToBase64String(new byte[32]),
                    algorithm = "ED25519"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/verification-bundles/verify", bundle);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        // The tampered root should cause the receipt merkle root consistency check to fail
        result.GetProperty("isValid").GetBoolean().Should().BeFalse();
        result.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.GetArrayLength().Should().BeGreaterThan(0);
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
        var merkleRoot = ComputeSha256Hex(txId);

        return new TransactionReceipt
        {
            ReceiptId = $"receipt-{Guid.NewGuid():N}",
            TransactionId = txId,
            RegisterId = registerId,
            DocketNumber = docketNumber,
            MerkleRoot = merkleRoot,
            InclusionProof = new MerkleInclusionProof
            {
                TransactionHash = merkleRoot,
                DocketNumber = docketNumber,
                MerkleRoot = merkleRoot,
                ProofPath = new List<MerkleProofStep>
                {
                    new MerkleProofStep
                    {
                        Hash = new string('c', 64),
                        Position = ProofPosition.Right
                    }
                }.AsReadOnly(),
                LeafIndex = 0,
                TreeSize = 2
            },
            Signatures = new List<ReceiptSignature>
            {
                new ReceiptSignature
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

    private static string ComputeSha256Hex(string input)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
