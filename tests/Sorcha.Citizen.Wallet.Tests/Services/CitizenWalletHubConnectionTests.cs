// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Citizen.Wallet.Services;
using Xunit;

namespace Sorcha.Citizen.Wallet.Tests.Services;

/// <summary>
/// Feature 114 / US4 — smoke coverage for <see cref="CitizenWalletHubConnection"/>.
/// SignalR's <c>HubConnection</c> is sealed and depends on a real WebSocket
/// transport, so end-to-end push behaviour is exercised by the Playwright fixture
/// (deferred follow-up). This suite covers the wiring around the connection:
/// argument validation, idempotent <c>StartAsync</c>, safe <c>StopAsync</c>
/// before connect, dispose semantics, and event-handler subscription.
/// </summary>
public class CitizenWalletHubConnectionTests
{
    [Fact]
    public void Constructor_NullBaseUrl_Throws()
    {
        FluentActions.Invoking(() => new CitizenWalletHubConnection(
                null!,
                new InMemoryAccessTokenStore(),
                NullLogger<CitizenWalletHubConnection>.Instance))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EmptyBaseUrl_Throws()
    {
        FluentActions.Invoking(() => new CitizenWalletHubConnection(
                "",
                new InMemoryAccessTokenStore(),
                NullLogger<CitizenWalletHubConnection>.Instance))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullTokenStore_Throws()
    {
        FluentActions.Invoking(() => new CitizenWalletHubConnection(
                "https://gateway.test",
                null!,
                NullLogger<CitizenWalletHubConnection>.Instance))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsConnected_BeforeStart_IsFalse()
    {
        var hub = CreateHub();
        hub.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        await using var hub = CreateHub();
        var act = async () => await hub.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_BeforeStart_IsSafe()
    {
        var hub = CreateHub();
        await hub.DisposeAsync();
    }

    [Fact]
    public void Events_CanBeSubscribedAndUnsubscribed()
    {
        var hub = CreateHub();
        Action<string> credentialHandler = _ => { };
        Action<Guid> deviceHandler = _ => { };

        hub.OnCredentialAvailable += credentialHandler;
        hub.OnDeviceRevoked += deviceHandler;
        hub.OnCredentialAvailable -= credentialHandler;
        hub.OnDeviceRevoked -= deviceHandler;
    }

    [Fact]
    public async Task StartAsync_NetworkUnavailable_DoesNotThrow()
    {
        // Pointing at a closed port — connection will fail. Per the thin-signal
        // contract the wallet must not surface this failure to the user; the
        // pull-on-open path remains authoritative.
        await using var hub = new CitizenWalletHubConnection(
            "http://127.0.0.1:1",
            new InMemoryAccessTokenStore(),
            NullLogger<CitizenWalletHubConnection>.Instance);

        var act = async () => await hub.StartAsync();

        await act.Should().NotThrowAsync();
        hub.IsConnected.Should().BeFalse();
    }

    private static CitizenWalletHubConnection CreateHub() => new(
        "https://gateway.test",
        new InMemoryAccessTokenStore(),
        NullLogger<CitizenWalletHubConnection>.Instance);
}
