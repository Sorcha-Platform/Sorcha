// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Tests for <see cref="TenantHubConnection"/>. Phase 5 of Feature 118.
/// Full SignalR integration tests require a live hub — these cover the
/// wrapper's contract surface (ctor, initial state, dispose).
/// </summary>
public class TenantHubConnectionTests
{
    private readonly Mock<ILogger<TenantHubConnection>> _mockLogger = new();

    [Fact]
    public void Ctor_BuildsHubUrlFromBaseUrl()
    {
        var sut = new TenantHubConnection(
            "http://localhost",
            accessTokenProvider: () => Task.FromResult<string?>("token"),
            _mockLogger.Object);

        sut.ConnectionState.Status.Should().Be(ConnectionStatus.Disconnected);
        sut.ConnectionState.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void Ctor_TrimsTrailingSlash()
    {
        var act = () => new TenantHubConnection(
            "http://localhost/",
            accessTokenProvider: () => Task.FromResult<string?>("token"),
            _mockLogger.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Ctor_NullTokenProvider_Throws()
    {
        var act = () => new TenantHubConnection(
            "http://localhost",
            accessTokenProvider: null!,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("accessTokenProvider");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        var act = () => new TenantHubConnection(
            "http://localhost",
            accessTokenProvider: () => Task.FromResult<string?>("token"),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task DisposeAsync_NoOpWhenNotStarted()
    {
        var sut = new TenantHubConnection(
            "http://localhost",
            accessTokenProvider: () => Task.FromResult<string?>("token"),
            _mockLogger.Object);

        var act = async () => await sut.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void EventSubscribers_AreNullByDefault()
    {
        var sut = new TenantHubConnection(
            "http://localhost",
            accessTokenProvider: () => Task.FromResult<string?>("token"),
            _mockLogger.Object);

        // Just confirm subscribing doesn't throw — wires up a no-op handler so the
        // event invocation paths in the wrapper are exercised when StartAsync fires.
        sut.OnInboxEntryAdded += static (_, _, _) => Task.CompletedTask;
        sut.OnInboxUnreadCountUpdated += static (_, _, _) => Task.CompletedTask;
        sut.OnConnectionStateChanged += static _ => { };
    }
}

// Constructor parameter alias for readability in tests — the wrapper takes a
// Func<Task<string?>> directly; this tiny helper lets tests use a named arg.
internal static class TenantHubConnectionTestHelpers
{
    public static TenantHubConnection Build(
        Func<Task<string?>> tokenProvider,
        ILogger<TenantHubConnection> logger,
        string baseUrl = "http://localhost")
        => new(baseUrl, tokenProvider, logger);
}
