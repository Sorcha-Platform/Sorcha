// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Integration tests for SignalR real-time notifications.
/// Tests thin signal notification delivery via BlueprintHub.
/// </summary>
public class SignalRIntegrationTests : IClassFixture<BlueprintServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly BlueprintServiceWebApplicationFactory _factory;
    private readonly List<HubConnection> _connections = new();

    public SignalRIntegrationTests(BlueprintServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        // Clean up all connections
        foreach (var connection in _connections)
        {
            if (connection.State == HubConnectionState.Connected)
            {
                await connection.StopAsync();
            }
            await connection.DisposeAsync();
        }
        _connections.Clear();
    }

    #region Hub Connection Tests

    [Fact]
    public async Task Hub_CanConnectAndDisconnect_Successfully()
    {
        // Arrange
        var connection = CreateHubConnection();

        // Act - Connect
        await connection.StartAsync();

        // Assert - Connected
        connection.State.Should().Be(HubConnectionState.Connected);

        // Act - Disconnect
        await connection.StopAsync();

        // Assert - Disconnected
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task Hub_MultipleClients_CanConnectSimultaneously()
    {
        // Arrange
        var connection1 = CreateHubConnection();
        var connection2 = CreateHubConnection();
        var connection3 = CreateHubConnection();

        // Act
        await connection1.StartAsync();
        await connection2.StartAsync();
        await connection3.StartAsync();

        // Assert
        connection1.State.Should().Be(HubConnectionState.Connected);
        connection2.State.Should().Be(HubConnectionState.Connected);
        connection3.State.Should().Be(HubConnectionState.Connected);
    }

    #endregion

    #region Wallet Subscription Tests

    [Fact]
    public async Task SubscribeToWallet_WithValidAddress_SuccessfullySubscribes()
    {
        // Arrange
        var connection = CreateHubConnection();
        await connection.StartAsync();
        var walletAddress = "0x1234567890abcdef";

        // Act
        await connection.InvokeAsync("SubscribeToWallet", walletAddress);

        // Assert - No exception thrown means success
        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task SubscribeToWallet_WithEmptyAddress_ThrowsException()
    {
        // Arrange
        var connection = CreateHubConnection();
        await connection.StartAsync();

        // Act & Assert
        var act = async () => await connection.InvokeAsync("SubscribeToWallet", "");
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Wallet address cannot be empty*");
    }

    [Fact]
    public async Task SubscribeToWallet_MultipleWallets_AllowsMultipleSubscriptions()
    {
        // Arrange
        var connection = CreateHubConnection();
        await connection.StartAsync();
        var wallet1 = "0x1111111111111111";
        var wallet2 = "0x2222222222222222";

        // Act
        await connection.InvokeAsync("SubscribeToWallet", wallet1);
        await connection.InvokeAsync("SubscribeToWallet", wallet2);

        // Assert - No exception means both subscriptions succeeded
        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task UnsubscribeFromWallet_AfterSubscribe_SuccessfullyUnsubscribes()
    {
        // Arrange
        var connection = CreateHubConnection();
        await connection.StartAsync();
        var walletAddress = "0x1234567890abcdef";

        await connection.InvokeAsync("SubscribeToWallet", walletAddress);

        // Act
        await connection.InvokeAsync("UnsubscribeFromWallet", walletAddress);

        // Assert - No exception means unsubscribe succeeded
        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task UnsubscribeFromWallet_WithEmptyAddress_ThrowsException()
    {
        // Arrange
        var connection = CreateHubConnection();
        await connection.StartAsync();

        // Act & Assert
        var act = async () => await connection.InvokeAsync("UnsubscribeFromWallet", "");
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Wallet address cannot be empty*");
    }

    #endregion

    #region Notification Broadcast Tests

    [Fact]
    public async Task NotifyActionAvailable_ToSubscribedWallet_ReceivesSignal()
    {
        // Arrange
        var connection = CreateHubConnection();
        var walletAddress = "0xabcdef1234567890";
        var receivedSignal = Channel.CreateUnbounded<CapturedActionSignal>();

        connection.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (instanceId, actionId, occurredAt, traceId) =>
            {
                receivedSignal.Writer.TryWrite(new CapturedActionSignal(instanceId, actionId, occurredAt, traceId));
            });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToWallet", walletAddress);

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        // Act
        await notificationService.NotifyActionAvailableAsync("instance-001", walletAddress);

        // Assert
        var received = await receivedSignal.Reader.ReadAsync(
            new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        received.InstanceId.Should().Be("instance-001");
    }

    [Fact]
    public async Task NotifyActionRejected_ToSubscribedWallet_ReceivesSignal()
    {
        // Arrange
        var connection = CreateHubConnection();
        var walletAddress = "0xrejected654321";
        var receivedSignal = Channel.CreateUnbounded<CapturedActionSignal>();

        connection.On<string, string, DateTimeOffset, string>("ActionRejected",
            (instanceId, actionId, occurredAt, traceId) =>
            {
                receivedSignal.Writer.TryWrite(new CapturedActionSignal(instanceId, actionId, occurredAt, traceId));
            });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToWallet", walletAddress);

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        // Act
        await notificationService.NotifyActionRejectedAsync("instance-003", walletAddress);

        // Assert
        var received = await receivedSignal.Reader.ReadAsync(
            new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        received.InstanceId.Should().Be("instance-003");
    }

    [Fact]
    public async Task Notification_ToUnsubscribedWallet_DoesNotReceive()
    {
        // Arrange
        var connection = CreateHubConnection();
        var walletAddress = "0xunsubscribed999";
        var receivedSignal = Channel.CreateUnbounded<CapturedActionSignal>();

        connection.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (instanceId, actionId, occurredAt, traceId) =>
            {
                receivedSignal.Writer.TryWrite(new CapturedActionSignal(instanceId, actionId, occurredAt, traceId));
            });

        await connection.StartAsync();
        // Note: NOT subscribing to the wallet

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        // Act
        await notificationService.NotifyActionAvailableAsync("instance-nosub", walletAddress);

        // Assert - Should timeout because no notification received
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = false;
        try
        {
            await receivedSignal.Reader.ReadAsync(cts.Token);
            received = true;
        }
        catch (OperationCanceledException)
        {
            // Expected - timeout means notification was not received
        }

        received.Should().BeFalse("notification should not be received for unsubscribed wallet");
    }

    [Fact]
    public async Task Notification_AfterUnsubscribe_DoesNotReceive()
    {
        // Arrange
        var connection = CreateHubConnection();
        var walletAddress = "0xafterunsub888";
        var receivedSignal = Channel.CreateUnbounded<CapturedActionSignal>();

        connection.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (instanceId, actionId, occurredAt, traceId) =>
            {
                receivedSignal.Writer.TryWrite(new CapturedActionSignal(instanceId, actionId, occurredAt, traceId));
            });

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToWallet", walletAddress);
        await connection.InvokeAsync("UnsubscribeFromWallet", walletAddress);

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        // Act
        await notificationService.NotifyActionAvailableAsync("instance-unsub", walletAddress);

        // Assert - Should timeout
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = false;
        try
        {
            await receivedSignal.Reader.ReadAsync(cts.Token);
            received = true;
        }
        catch (OperationCanceledException)
        {
            // Expected - timeout means notification was not received
        }

        received.Should().BeFalse("notification should not be received after unsubscribing");
    }

    #endregion

    #region Multi-Client Broadcast Tests

    [Fact]
    public async Task Notification_ToMultipleSubscribedClients_AllReceive()
    {
        // Arrange
        var walletAddress = "0xmulticlient777";

        var connection1 = CreateHubConnection();
        var connection2 = CreateHubConnection();
        var connection3 = CreateHubConnection();

        var received1 = Channel.CreateUnbounded<CapturedActionSignal>();
        var received2 = Channel.CreateUnbounded<CapturedActionSignal>();
        var received3 = Channel.CreateUnbounded<CapturedActionSignal>();

        connection1.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (i, a, o, t) => received1.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));
        connection2.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (i, a, o, t) => received2.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));
        connection3.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (i, a, o, t) => received3.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));

        await connection1.StartAsync();
        await connection2.StartAsync();
        await connection3.StartAsync();

        await connection1.InvokeAsync("SubscribeToWallet", walletAddress);
        await connection2.InvokeAsync("SubscribeToWallet", walletAddress);
        await connection3.InvokeAsync("SubscribeToWallet", walletAddress);

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        // Act
        await notificationService.NotifyActionAvailableAsync("instance-multi", walletAddress);

        // Assert - All three clients should receive
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var n1 = await received1.Reader.ReadAsync(cts.Token);
        var n2 = await received2.Reader.ReadAsync(cts.Token);
        var n3 = await received3.Reader.ReadAsync(cts.Token);

        n1.InstanceId.Should().Be("instance-multi");
        n2.InstanceId.Should().Be("instance-multi");
        n3.InstanceId.Should().Be("instance-multi");
    }

    [Fact]
    public async Task Notification_ToSpecificWallet_OnlySubscribedWalletReceives()
    {
        // Arrange
        var wallet1 = "0xwallet1specific";
        var wallet2 = "0xwallet2specific";

        var connection1 = CreateHubConnection();
        var connection2 = CreateHubConnection();

        var received1 = Channel.CreateUnbounded<CapturedActionSignal>();
        var received2 = Channel.CreateUnbounded<CapturedActionSignal>();

        connection1.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (i, a, o, t) => received1.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));
        connection2.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (i, a, o, t) => received2.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));

        await connection1.StartAsync();
        await connection2.StartAsync();

        await connection1.InvokeAsync("SubscribeToWallet", wallet1);
        await connection2.InvokeAsync("SubscribeToWallet", wallet2);

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        // Act — only wallet1 should receive
        await notificationService.NotifyActionAvailableAsync("instance-specific", wallet1);

        // Assert
        var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var n1 = await received1.Reader.ReadAsync(cts1.Token);
        n1.InstanceId.Should().Be("instance-specific");

        // Connection2 should NOT receive
        var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var receivedByWallet2 = false;
        try
        {
            await received2.Reader.ReadAsync(cts2.Token);
            receivedByWallet2 = true;
        }
        catch (OperationCanceledException)
        {
            // Expected - wallet2 should not receive wallet1's notification
        }

        receivedByWallet2.Should().BeFalse("wallet2 should not receive wallet1's notification");
    }

    #endregion

    #region Both Notification Types Test

    [Fact]
    public async Task Hub_SupportsBothSignalTypes_Successfully()
    {
        // Arrange
        var connection = CreateHubConnection();
        var walletAddress = "0xalltypes123";

        var availableReceived = Channel.CreateUnbounded<CapturedActionSignal>();
        var rejectedReceived = Channel.CreateUnbounded<CapturedActionSignal>();

        connection.On<string, string, DateTimeOffset, string>("ActionAvailable",
            (i, a, o, t) => availableReceived.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));
        connection.On<string, string, DateTimeOffset, string>("ActionRejected",
            (i, a, o, t) => rejectedReceived.Writer.TryWrite(new CapturedActionSignal(i, a, o, t)));

        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToWallet", walletAddress);

        var notificationService = _factory.Services.GetRequiredService<INotificationService>();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act & Assert - ActionAvailable (method name is the discriminator on the wire).
        await notificationService.NotifyActionAvailableAsync("instance-avail", walletAddress);
        var available = await availableReceived.Reader.ReadAsync(cts.Token);
        available.InstanceId.Should().Be("instance-avail");

        // Act & Assert - ActionRejected
        await notificationService.NotifyActionRejectedAsync("instance-reject", walletAddress);
        var rejected = await rejectedReceived.Reader.ReadAsync(cts.Token);
        rejected.InstanceId.Should().Be("instance-reject");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Test-only capture record for the multi-arg ActionAvailable / ActionRejected wire shape.
    /// Mirrors the parameter list on <c>IBlueprintHubClient</c>.
    /// </summary>
    private sealed record CapturedActionSignal(
        string InstanceId, string ActionId, DateTimeOffset OccurredAt, string TraceId);

    private HubConnection CreateHubConnection()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{_factory.Server.BaseAddress}actionshub", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        _connections.Add(connection);
        return connection;
    }

    #endregion
}
