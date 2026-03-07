// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests;

public class TransactionApiTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly string _testRegisterId;

    public TransactionApiTests(RegisterServiceWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("API Test Register", "api-test-tenant").Result;
        _testRegisterId = register.Id;
    }

    [Fact]
    public async Task SubmitTransaction_WithValidData_ShouldReturn201Created()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = response.Headers.Location;
        location.Should().NotBeNull();
        location!.OriginalString.Should().Contain(_testRegisterId);
        location.OriginalString.Should().Contain("transactions");
    }

    [Fact]
    public async Task SubmitTransaction_ShouldReturnTransactionWithDIDUri()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel>();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().StartWith("did:sorcha:register:");
        result.Id.Should().Contain(_testRegisterId);
        result.Id.Should().Contain("/tx/");
        result.TxId.Should().Be(transaction.TxId);
    }

    [Fact]
    public async Task SubmitTransaction_WithInvalidTxId_ShouldReturn400BadRequest()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();
        transaction.TxId = "invalid";

        // Act
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitTransaction_WithMismatchedPayloadCount_ShouldReturn400BadRequest()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();
        transaction.PayloadCount = 5; // But only has 1 payload

        // Act
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTransaction_WithValidId_ShouldReturn200OK()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();
        var submitResponse = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<TransactionModel>();

        // Act
        var response = await _client.GetAsync($"/api/registers/{_testRegisterId}/transactions/{submitted!.TxId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel>();
        result!.TxId.Should().Be(submitted.TxId);
    }

    [Fact]
    public async Task GetTransaction_WithNonExistentId_ShouldReturn404NotFound()
    {
        // Arrange
        var nonExistentTxId = new string('0', 64);

        // Act
        var response = await _client.GetAsync($"/api/registers/{_testRegisterId}/transactions/{nonExistentTxId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTransactions_ShouldReturn200OK()
    {
        // Act
        var response = await _client.GetAsync($"/api/registers/{_testRegisterId}/transactions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTransactions_ShouldReturnPaginatedResults()
    {
        // Arrange
        await SubmitTestTransactionAsync();
        await SubmitTestTransactionAsync();
        await SubmitTestTransactionAsync();

        // Act
        var response = await _client.GetAsync($"/api/registers/{_testRegisterId}/transactions?$skip=0&$top=2");
        var result = await response.Content.ReadFromJsonAsync<PaginatedTransactionResponse>();

        // Assert
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.Transactions.Should().HaveCount(2);
        result.Total.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetTransactions_ShouldOrderByTimestampDescending()
    {
        // Arrange
        await SubmitTestTransactionAsync();
        await Task.Delay(100); // Ensure different timestamps
        await SubmitTestTransactionAsync();
        await Task.Delay(100);
        await SubmitTestTransactionAsync();

        // Act
        var response = await _client.GetAsync($"/api/registers/{_testRegisterId}/transactions?page=1&pageSize=10");
        var result = await response.Content.ReadFromJsonAsync<PaginatedTransactionResponse>();

        // Assert
        result!.Transactions.Should().BeInDescendingOrder(t => t.TimeStamp);
    }

    [Fact]
    public async Task SubmitAndRetrieveTransaction_EndToEndWorkflow()
    {
        // Create
        var transaction = CreateValidTransactionRequest();
        transaction.MetaData = new TransactionMetaData
        {
            RegisterId = _testRegisterId,
            TransactionType = Models.Enums.TransactionType.Action,
            BlueprintId = "blueprint123",
            InstanceId = "instance456",
            ActionId = 1
        };

        var submitResponse = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<TransactionModel>();

        // Retrieve
        var getResponse = await _client.GetAsync($"/api/registers/{_testRegisterId}/transactions/{submitted!.TxId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retrieved = await getResponse.Content.ReadFromJsonAsync<TransactionModel>();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.TxId.Should().Be(submitted.TxId);
        retrieved.SenderWallet.Should().Be(transaction.SenderWallet);
        retrieved.MetaData.Should().NotBeNull();
        retrieved.MetaData!.BlueprintId.Should().Be("blueprint123");
        retrieved.MetaData.InstanceId.Should().Be("instance456");
    }

    [Fact]
    public async Task SubmitTransaction_WithPayloads_ShouldPreservePayloads()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();
        transaction.PayloadCount = 2;
        transaction.Payloads = new[]
        {
            new PayloadModel
            {
                WalletAccess = new[] { "wallet1", "wallet2" },
                PayloadSize = 1024,
                Hash = "hash1",
                Data = "encrypted_data_1",
                PayloadFlags = "encrypted"
            },
            new PayloadModel
            {
                WalletAccess = new[] { "wallet3" },
                PayloadSize = 2048,
                Hash = "hash2",
                Data = "encrypted_data_2",
                PayloadFlags = "encrypted,compressed"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel>();

        // Assert
        result!.Payloads.Should().HaveCount(2);
        result.Payloads[0].Hash.Should().Be("hash1");
        result.Payloads[0].WalletAccess.Should().Contain("wallet1");
        result.Payloads[1].Hash.Should().Be("hash2");
        result.Payloads[1].PayloadSize.Should().Be(2048);
    }

    [Fact]
    public async Task SubmitTransaction_WithMultipleRecipients_ShouldPreserveRecipients()
    {
        // Arrange
        var transaction = CreateValidTransactionRequest();
        transaction.RecipientsWallets = new[] { "recipient1", "recipient2", "recipient3" };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel>();

        // Assert
        result!.RecipientsWallets.Should().HaveCount(3);
        result.RecipientsWallets.Should().Contain("recipient1");
        result.RecipientsWallets.Should().Contain("recipient2");
        result.RecipientsWallets.Should().Contain("recipient3");
    }

    private async Task<TransactionModel> SubmitTestTransactionAsync()
    {
        var transaction = CreateValidTransactionRequest();
        var response = await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", transaction);
        return (await response.Content.ReadFromJsonAsync<TransactionModel>())!;
    }

    private TransactionModel CreateValidTransactionRequest()
    {
        var txId = Guid.NewGuid().ToString("N") + new string('0', 64);
        txId = txId.Substring(0, 64);

        return new TransactionModel
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
    }

    private record RegisterResponse(string Id, string Name, uint Height);

    private record PaginatedTransactionResponse(
        int Page,
        int PageSize,
        int Total,
        TransactionModel[] Transactions);
}
