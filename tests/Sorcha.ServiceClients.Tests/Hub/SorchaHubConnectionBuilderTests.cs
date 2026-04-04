// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.ServiceClients.Http.Hub;
using Xunit;

namespace Sorcha.ServiceClients.Tests.Hub;

public class SorchaHubConnectionBuilderTests
{
    [Fact]
    public void Build_WithValidParameters_ReturnsNonNullConnection()
    {
        var connection = SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            () => Task.FromResult<string?>("test-token"));

        connection.Should().NotBeNull();
    }

    [Fact]
    public void Build_ReturnsDisconnectedConnection()
    {
        var connection = SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            () => Task.FromResult<string?>("test-token"));

        connection.State.Should().Be(HubConnectionState.Disconnected,
            "connection should not be started automatically");
    }

    [Fact]
    public void Build_WithNullHubUrl_ThrowsArgumentNullException()
    {
        var act = () => SorchaHubConnectionBuilder.Build(
            null!,
            () => Task.FromResult<string?>("test-token"));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("hubUrl");
    }

    [Fact]
    public void Build_WithNullTokenProvider_ThrowsArgumentNullException()
    {
        var act = () => SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("tokenProvider");
    }

    [Fact]
    public void Build_WithLoggingConfiguration_DoesNotThrow()
    {
        var act = () => SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            () => Task.FromResult<string?>("test-token"),
            logging => { });

        act.Should().NotThrow();
    }
}
