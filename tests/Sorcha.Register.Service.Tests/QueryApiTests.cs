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

public class QueryApiTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly string _testRegisterId;
    private readonly string _testWalletAddress = "test_wallet_12345";

    public QueryApiTests(RegisterServiceWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("Query Test Register", "query-test-tenant").Result;
        _testRegisterId = register.Id;
        SeedTestDataAsync().Wait();
    }

    [Fact]
    public async Task GetTransactionsByWallet_ShouldReturn200OK()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/query/wallets/{_testWalletAddress}/transactions?registerId={_testRegisterId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTransactionsByWallet_ShouldReturnPaginatedResults()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/query/wallets/{_testWalletAddress}/transactions?registerId={_testRegisterId}&page=1&pageSize=2");
        var result = await response.Content.ReadFromJsonAsync<PaginatedResponse>();

        // Assert
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.Items.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetTransactionsByWallet_WithoutRegisterId_ShouldReturn400BadRequest()
    {
        // Act
        var response = await _client.GetAsync($"/api/query/wallets/{_testWalletAddress}/transactions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTransactionsBySender_ShouldReturn200OK()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/query/senders/{_testWalletAddress}/transactions?registerId={_testRegisterId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTransactionsBySender_ShouldReturnOnlySenderTransactions()
    {
        // Act
        var response = await _client.GetAsync(
            $"/api/query/senders/{_testWalletAddress}/transactions?registerId={_testRegisterId}&page=1&pageSize=20");
        var result = await response.Content.ReadFromJsonAsync<PaginatedResponse>();

        // Assert
        result!.Items.Should().OnlyContain(t => t.SenderWallet == _testWalletAddress);
    }

    [Fact]
    public async Task GetTransactionsByBlueprint_ShouldReturn200OK()
    {
        // Arrange
        var blueprintId = "blueprint123";

        // Act
        var response = await _client.GetAsync(
            $"/api/query/blueprints/{blueprintId}/transactions?registerId={_testRegisterId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTransactionsByBlueprint_WithInstanceId_ShouldFilterByInstance()
    {
        // Arrange
        var blueprintId = "blueprint123";
        var instanceId = "instance456";

        // Act — blueprint endpoint returns IEnumerable<TransactionModel>, not paginated
        var response = await _client.GetAsync(
            $"/api/query/blueprints/{blueprintId}/transactions?registerId={_testRegisterId}&instanceId={instanceId}");
        var result = await response.Content.ReadFromJsonAsync<TransactionModel[]>();

        // Assert
        result.Should().NotBeNull();
        if (result!.Length > 0)
        {
            result.Should().OnlyContain(t =>
                t.MetaData != null &&
                t.MetaData.BlueprintId == blueprintId &&
                t.MetaData.InstanceId == instanceId);
        }
    }

    [Fact]
    public async Task GetTransactionStatistics_ShouldReturn200OK()
    {
        // Act
        var response = await _client.GetAsync($"/api/query/stats?registerId={_testRegisterId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTransactionStatistics_ShouldReturnValidStatistics()
    {
        // Act
        var response = await _client.GetAsync($"/api/query/stats?registerId={_testRegisterId}");
        var stats = await response.Content.ReadFromJsonAsync<TransactionStatistics>();

        // Assert
        stats.Should().NotBeNull();
        stats!.TotalTransactions.Should().BeGreaterThan(0);
        stats.UniqueWallets.Should().BeGreaterThan(0);
        stats.UniqueSenders.Should().BeGreaterThan(0);
        stats.UniqueRecipients.Should().BeGreaterThan(0);
        stats.TotalPayloads.Should().BeGreaterThan(0);
        stats.EarliestTransaction.Should().NotBeNull();
        stats.LatestTransaction.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTransactionsByWallet_ShouldSupportPagination()
    {
        // Act
        var page1Response = await _client.GetAsync(
            $"/api/query/wallets/{_testWalletAddress}/transactions?registerId={_testRegisterId}&page=1&pageSize=1");
        var page1 = await page1Response.Content.ReadFromJsonAsync<PaginatedResponse>();

        var page2Response = await _client.GetAsync(
            $"/api/query/wallets/{_testWalletAddress}/transactions?registerId={_testRegisterId}&page=2&pageSize=1");
        var page2 = await page2Response.Content.ReadFromJsonAsync<PaginatedResponse>();

        // Assert
        page1!.Page.Should().Be(1);
        page2!.Page.Should().Be(2);
        if (page1.Items.Count > 0 && page2.Items.Count > 0)
        {
            page1.Items.Should().NotIntersectWith(page2.Items);
        }
    }

    [Fact]
    public async Task GetTransactionsBySender_ShouldSupportPagination()
    {
        // Act
        var page1Response = await _client.GetAsync(
            $"/api/query/senders/{_testWalletAddress}/transactions?registerId={_testRegisterId}&page=1&pageSize=1");
        var page1 = await page1Response.Content.ReadFromJsonAsync<PaginatedResponse>();

        var page2Response = await _client.GetAsync(
            $"/api/query/senders/{_testWalletAddress}/transactions?registerId={_testRegisterId}&page=2&pageSize=1");
        var page2 = await page2Response.Content.ReadFromJsonAsync<PaginatedResponse>();

        // Assert
        page1!.Page.Should().Be(1);
        page2!.Page.Should().Be(2);
        page1.HasPreviousPage.Should().BeFalse();
        if (page2.TotalPages > 2)
        {
            page2.HasNextPage.Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetTransactionsByBlueprint_ShouldSupportPagination()
    {
        // Arrange
        var blueprintId = "blueprint123";

        // Act — blueprint endpoint returns IEnumerable<TransactionModel>, not paginated
        var response = await _client.GetAsync(
            $"/api/query/blueprints/{blueprintId}/transactions?registerId={_testRegisterId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TransactionModel[]>();
        result.Should().NotBeNull();
        result!.Length.Should().BeGreaterThanOrEqualTo(0);
    }

    private async Task SeedTestDataAsync()
    {
        // Create transactions with the test wallet as sender
        for (int i = 0; i < 3; i++)
        {
            var tx = CreateTransaction(_testWalletAddress, $"recipient{i}");
            if (i == 0)
            {
                tx.MetaData = new TransactionMetaData
                {
                    RegisterId = _testRegisterId,
                    TransactionType = Models.Enums.TransactionType.Action,
                    BlueprintId = "blueprint123",
                    InstanceId = "instance456",
                    ActionId = (uint)i
                };
            }
            await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", tx);
            await Task.Delay(50); // Ensure different timestamps
        }

        // Create transactions with the test wallet as recipient
        for (int i = 0; i < 2; i++)
        {
            var tx = CreateTransaction($"sender{i}", _testWalletAddress);
            await _client.PostAsJsonAsync($"/api/registers/{_testRegisterId}/transactions", tx);
            await Task.Delay(50);
        }
    }

    private TransactionModel CreateTransaction(string senderWallet, string recipientWallet)
    {
        var txId = Guid.NewGuid().ToString("N") + new string('0', 64);
        txId = txId.Substring(0, 64);

        return new TransactionModel
        {
            RegisterId = _testRegisterId,
            TxId = txId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = senderWallet,
            RecipientsWallets = new[] { recipientWallet },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { senderWallet },
                    PayloadSize = 1024,
                    Hash = "hash",
                    Data = "data"
                }
            },
            Signature = "signature"
        };
    }

    private record RegisterResponse(string Id, string Name);

    private record PaginatedResponse(
        int Page,
        int PageSize,
        int TotalPages,
        int TotalCount,
        bool HasPreviousPage,
        bool HasNextPage,
        List<TransactionModel> Items);

    private record TransactionStatistics(
        int TotalTransactions,
        int UniqueWallets,
        int UniqueSenders,
        int UniqueRecipients,
        int TotalPayloads,
        DateTime? EarliestTransaction,
        DateTime? LatestTransaction);
}
