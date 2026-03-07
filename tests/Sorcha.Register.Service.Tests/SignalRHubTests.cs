// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests;

public class SignalRHubTests : IClassFixture<RegisterServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private HubConnection? _hubConnection;
    private readonly List<string> _receivedMessages = new();

    public SignalRHubTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async ValueTask InitializeAsync()
    {
        var hubUrl = _factory.Server.BaseAddress + "hubs/register";
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await _hubConnection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
        _client.Dispose();
    }

    [Fact]
    public async Task HubConnection_ShouldConnectSuccessfully()
    {
        // Assert
        _hubConnection!.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task SubscribeToRegister_ShouldAllowSubscription()
    {
        // Arrange
        var registerId = "test-register-123";

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            await _hubConnection!.InvokeAsync("SubscribeToRegister", registerId));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public async Task UnsubscribeFromRegister_ShouldAllowUnsubscription()
    {
        // Arrange
        var registerId = "test-register-123";
        await _hubConnection!.InvokeAsync("SubscribeToRegister", registerId);

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            await _hubConnection.InvokeAsync("UnsubscribeFromRegister", registerId));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeToTenant_ShouldAllowSubscription()
    {
        // Arrange
        var tenantId = "test-tenant-123";

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            await _hubConnection!.InvokeAsync("SubscribeToTenant", tenantId));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public async Task UnsubscribeFromTenant_ShouldAllowUnsubscription()
    {
        // Arrange
        var tenantId = "test-tenant-123";
        await _hubConnection!.InvokeAsync("SubscribeToTenant", tenantId);

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            await _hubConnection.InvokeAsync("UnsubscribeFromTenant", tenantId));

        // Assert
        exception.Should().BeNull();
    }

    [Fact]
    public async Task RegisterCreated_ShouldReceiveEvent()
    {
        // Arrange
        var tenantId = "signalr-test-tenant";
        var registerCreatedReceived = false;
        string? receivedRegisterId = null;
        string? receivedName = null;

        _hubConnection!.On<string, string>("RegisterCreated", (registerId, name) =>
        {
            registerCreatedReceived = true;
            receivedRegisterId = registerId;
            receivedName = name;
        });

        await _hubConnection.InvokeAsync("SubscribeToTenant", tenantId);

        // Act — create register via service layer (POST /api/registers was removed)
        var register = await _factory.CreateTestRegisterAsync("SignalR Test Register", tenantId);

        // Wait for event
        await Task.Delay(1000);

        // Assert
        registerCreatedReceived.Should().BeTrue();
        receivedRegisterId.Should().Be(register.Id);
        receivedName.Should().Be("SignalR Test Register");
    }

    [Fact]
    public async Task RegisterDeleted_ShouldReceiveEvent()
    {
        // Arrange
        var tenantId = "signalr-delete-tenant";
        var registerDeletedReceived = false;
        string? receivedRegisterId = null;

        _hubConnection!.On<string>("RegisterDeleted", (registerId) =>
        {
            registerDeletedReceived = true;
            receivedRegisterId = registerId;
        });

        await _hubConnection.InvokeAsync("SubscribeToTenant", tenantId);

        // Create a register via service layer (POST /api/registers was removed)
        var register = await _factory.CreateTestRegisterAsync("To Delete", tenantId);

        await Task.Delay(500); // Allow create event to process

        // Act
        await _client.DeleteAsync($"/api/registers/{register.Id}?tenantId={tenantId}");

        // Wait for event
        await Task.Delay(1000);

        // Assert
        registerDeletedReceived.Should().BeTrue();
        receivedRegisterId.Should().Be(register.Id);
    }

    [Fact]
    public async Task TransactionConfirmed_ShouldReceiveEvent()
    {
        // Arrange
        var transactionConfirmedReceived = false;
        string? receivedRegisterId = null;
        string? receivedTransactionId = null;

        _hubConnection!.On<string, string>("TransactionConfirmed", (registerId, transactionId) =>
        {
            transactionConfirmedReceived = true;
            receivedRegisterId = registerId;
            receivedTransactionId = transactionId;
        });

        // Create a register via service layer (POST /api/registers was removed)
        var register = await _factory.CreateTestRegisterAsync("SignalR Tx Test", "tx-test-tenant");

        await _hubConnection!.InvokeAsync("SubscribeToRegister", register.Id);

        // Act
        var transaction = CreateValidTransaction(register.Id);
        var txResponse = await _client.PostAsJsonAsync($"/api/registers/{register.Id}/transactions", transaction);
        var submittedTx = await txResponse.Content.ReadFromJsonAsync<TransactionModel>();

        // Wait for event
        await Task.Delay(1000);

        // Assert
        transactionConfirmedReceived.Should().BeTrue();
        receivedRegisterId.Should().Be(register.Id);
        receivedTransactionId.Should().Be(submittedTx!.TxId);
    }

    [Fact]
    public async Task MultipleClients_ShouldReceiveSameEvent()
    {
        // Arrange
        var tenantId = "multi-client-tenant";

        // Create second hub connection
        var hubUrl = _factory.Server.BaseAddress + "hubs/register";
        var hubConnection2 = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();
        await hubConnection2.StartAsync();

        var client1Received = false;
        var client2Received = false;

        _hubConnection!.On<string, string>("RegisterCreated", (_, _) => client1Received = true);
        hubConnection2.On<string, string>("RegisterCreated", (_, _) => client2Received = true);

        await _hubConnection.InvokeAsync("SubscribeToTenant", tenantId);
        await hubConnection2.InvokeAsync("SubscribeToTenant", tenantId);

        // Act — create register via service layer (POST /api/registers was removed)
        await _factory.CreateTestRegisterAsync("Multi-Client Test", tenantId);

        // Wait for events
        await Task.Delay(1000);

        // Assert
        client1Received.Should().BeTrue();
        client2Received.Should().BeTrue();

        // Cleanup
        await hubConnection2.DisposeAsync();
    }

    [Fact]
    public async Task UnsubscribedClient_ShouldNotReceiveEvent()
    {
        // Arrange
        var tenantId = "unsubscribed-tenant";
        var eventReceived = false;

        _hubConnection!.On<string, string>("RegisterCreated", (_, _) => eventReceived = true);

        // Don't subscribe to tenant

        // Act — create register via service layer (POST /api/registers was removed)
        await _factory.CreateTestRegisterAsync("Unsubscribed Test", tenantId);

        // Wait
        await Task.Delay(1000);

        // Assert
        eventReceived.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterSubscription_ShouldOnlyReceiveRegisterSpecificEvents()
    {
        // Arrange
        // Create two registers first so we have their actual IDs
        var reg1 = await CreateTestRegisterAsync("Register 1", "test-tenant");
        var reg2 = await CreateTestRegisterAsync("Register 2", "test-tenant");

        var register1ReceivedEvent = false;
        var register2ReceivedEvent = false;

        _hubConnection!.On<string, string>("TransactionConfirmed", (registerId, _) =>
        {
            if (registerId == reg1.Id) register1ReceivedEvent = true;
            if (registerId == reg2.Id) register2ReceivedEvent = true;
        });

        // Subscribe only to register 1
        await _hubConnection.InvokeAsync("SubscribeToRegister", reg1.Id);

        // Act - Submit transaction to register 1
        var tx = CreateValidTransaction(reg1.Id);
        await _client.PostAsJsonAsync($"/api/registers/{reg1.Id}/transactions", tx);

        await Task.Delay(1000);

        // Assert
        register1ReceivedEvent.Should().BeTrue();
        register2ReceivedEvent.Should().BeFalse();
    }

    private async Task<RegisterResponse> CreateTestRegisterAsync(string name, string tenantId)
    {
        var register = await _factory.CreateTestRegisterAsync(name, tenantId);
        return new RegisterResponse(register.Id, register.Name, register.TenantId);
    }

    private TransactionModel CreateValidTransaction(string registerId)
    {
        var txId = Guid.NewGuid().ToString("N") + new string('0', 64);
        txId = txId.Substring(0, 64);

        return new TransactionModel
        {
            RegisterId = registerId,
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
                    PayloadSize = 1024,
                    Hash = "hash",
                    Data = "data"
                }
            },
            Signature = "signature"
        };
    }

    private record RegisterResponse(string Id, string Name, string TenantId);
}
