// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for revocation and transaction status endpoints (T039).
/// Covers POST revoke transaction, and GET transaction lifecycle status.
/// </summary>
public class RevocationEndpointTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _testRegisterId;

    public RevocationEndpointTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("Revocation Test Register", "revocation-test-tenant").Result;
        _testRegisterId = register.Id;
    }

    // ===========================
    // POST /api/registers/{registerId}/transactions/revoke
    // ===========================

    [Fact]
    public async Task RevokeTransaction_WithValidRequest_ShouldReturn202Accepted()
    {
        // Arrange — submit a transaction to revoke
        var txId = await SubmitTestTransactionAsync();

        var revokeRequest = new
        {
            originalTxId = txId,
            reason = "Erroneous",
            signerWalletAddress = "signer-wallet-001"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("originalTxId").GetString().Should().Be(txId);
        result.GetProperty("status").GetString().Should().Be("submitted");
        result.GetProperty("revocationTxId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RevokeTransaction_WithNonExistentTarget_ShouldReturn404()
    {
        // Arrange
        var revokeRequest = new
        {
            originalTxId = new string('a', 64),
            reason = "Erroneous"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeTransaction_AlreadyRevoked_ShouldReturn409Conflict()
    {
        // Arrange — submit and then revoke a transaction
        var txId = await SubmitTestTransactionAsync();

        var revokeRequest = new
        {
            originalTxId = txId,
            reason = "Erroneous"
        };

        // First revocation should succeed
        var firstResponse = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act — attempt to revoke the same transaction again
        var secondResponse = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var result = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("error").GetString().Should().Contain("already revoked");
    }

    [Fact]
    public async Task RevokeTransaction_WithInvalidReason_ShouldReturn400()
    {
        // Arrange
        var txId = await SubmitTestTransactionAsync();

        var revokeRequest = new
        {
            originalTxId = txId,
            reason = "InvalidReasonValue"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokeTransaction_WithSupersededReason_ShouldReturn202()
    {
        // Arrange — submit two transactions: original and superseding
        var originalTxId = await SubmitTestTransactionAsync();
        var supersedingTxId = await SubmitTestTransactionAsync();

        var revokeRequest = new
        {
            originalTxId,
            reason = "Superseded",
            supersededByTxId = supersedingTxId
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ===========================
    // GET /api/registers/{registerId}/transactions/{txId}/status
    // ===========================

    [Fact]
    public async Task GetTransactionStatus_ActiveTransaction_ShouldReturnActiveStatus()
    {
        // Arrange
        var txId = await SubmitTestTransactionAsync();

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("transactionId").GetString().Should().Be(txId);
        result.GetProperty("status").GetInt32().Should().Be((int)TransactionLifecycleStatus.Active);
    }

    [Fact]
    public async Task GetTransactionStatus_RevokedTransaction_ShouldReturnRevokedStatus()
    {
        // Arrange — submit and revoke a transaction
        var txId = await SubmitTestTransactionAsync();

        var revokeRequest = new
        {
            originalTxId = txId,
            reason = "Compromised"
        };
        var revokeResponse = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{txId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("transactionId").GetString().Should().Be(txId);
        result.GetProperty("status").GetInt32().Should().Be((int)TransactionLifecycleStatus.Revoked);
        result.GetProperty("revocationTxId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetTransactionStatus_SupersededTransaction_ShouldReturnSupersededStatus()
    {
        // Arrange — submit original and superseding, then revoke as superseded
        var originalTxId = await SubmitTestTransactionAsync();
        var supersedingTxId = await SubmitTestTransactionAsync();

        var revokeRequest = new
        {
            originalTxId,
            reason = "Superseded",
            supersededByTxId = supersedingTxId
        };
        var revokeResponse = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/transactions/revoke", revokeRequest);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{originalTxId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("transactionId").GetString().Should().Be(originalTxId);
        result.GetProperty("status").GetInt32().Should().Be((int)TransactionLifecycleStatus.Superseded);
        result.GetProperty("supersededByTxId").GetString().Should().Be(supersedingTxId);
    }

    [Fact]
    public async Task GetTransactionStatus_NonExistentTransaction_ShouldReturn404()
    {
        // Arrange
        var nonExistentTxId = new string('e', 64);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/transactions/{nonExistentTxId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
}
