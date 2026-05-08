// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Storage.InMemory;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 119 — T010 unit tests for <see cref="PresentationSealSubscriber"/>.
/// Verifies subscription wiring + sweep-cadence ticking using
/// <see cref="InMemoryEventSubscriber"/> and a mocked coordinator.
/// </summary>
public class PresentationSealSubscriberTests
{
    [Fact]
    public async Task ExecuteAsync_OnTransactionConfirmedEvent_DrainsCoordinator()
    {
        var subscriber = new InMemoryEventSubscriber();
        var publisher = new InMemoryEventPublisher(subscriber);
        var coordinator = new Mock<IPresentationSealCoordinator>();
        coordinator.Setup(c => c.DrainOnSealAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        coordinator.Setup(c => c.RunRecoverySweepAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SweepResult(0, 0));

        var options = Options.Create(new PresentationLifecycleOptions
        {
            SealRecoverySweepIntervalSeconds = 60   // long, so the sweep tick doesn't fire during the test
        });
        var sut = new PresentationSealSubscriber(
            subscriber, coordinator.Object, options,
            new Mock<ILogger<PresentationSealSubscriber>>().Object);

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);

        // Wait for subscription to register.
        for (var i = 0; i < 50 && subscriber.GetSubscriptionCount(RegisterEventChannels.TransactionConfirmed) == 0; i++)
        {
            await Task.Delay(20);
        }
        subscriber.GetSubscriptionCount(RegisterEventChannels.TransactionConfirmed)
            .Should().BeGreaterThan(0, "subscriber should register on the transaction:confirmed channel");

        // Publish a TransactionConfirmedEvent.
        await publisher.PublishAsync(RegisterEventChannels.TransactionConfirmed, new TransactionConfirmedEvent
        {
            TransactionId = "tx-abc",
            RegisterId = "reg-1",
            SenderWallet = "ws11qsender",
            PreviousTransactionId = "tx-prev",
            ConfirmedAt = DateTime.UtcNow
        });

        // The handler runs synchronously inside the in-memory dispatcher — single Verify is sufficient.
        await Task.Delay(50);

        coordinator.Verify(c => c.DrainOnSealAsync("tx-abc", It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await cts.CancelAsync();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ExecuteAsync_RunsRecoverySweep_OnInterval()
    {
        var subscriber = new InMemoryEventSubscriber();
        var coordinator = new Mock<IPresentationSealCoordinator>();
        coordinator.Setup(c => c.RunRecoverySweepAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SweepResult(0, 0));

        var options = Options.Create(new PresentationLifecycleOptions
        {
            SealRecoverySweepIntervalSeconds = 1
        });
        var sut = new PresentationSealSubscriber(
            subscriber, coordinator.Object, options,
            new Mock<ILogger<PresentationSealSubscriber>>().Object);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);

        // Wait for at least two sweep ticks.
        await Task.Delay(2500);

        coordinator.Verify(c => c.RunRecoverySweepAsync(It.IsAny<CancellationToken>()), Times.AtLeast(1));

        await cts.CancelAsync();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ExecuteAsync_GracefulShutdown_OnStoppingTokenCancelled()
    {
        var subscriber = new InMemoryEventSubscriber();
        var coordinator = new Mock<IPresentationSealCoordinator>();
        coordinator.Setup(c => c.RunRecoverySweepAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SweepResult(0, 0));
        var options = Options.Create(new PresentationLifecycleOptions { SealRecoverySweepIntervalSeconds = 60 });
        var sut = new PresentationSealSubscriber(
            subscriber, coordinator.Object, options,
            new Mock<ILogger<PresentationSealSubscriber>>().Object);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await cts.CancelAsync();

        // Should complete without throwing.
        await sut.StopAsync(CancellationToken.None);
    }
}
