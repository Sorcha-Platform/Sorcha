// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Integration.Tests.Infrastructure;

/// <summary>
/// Test fixture for peer-to-peer integration tests.
/// Provides multiple peer instances for testing REST and gRPC endpoints.
/// </summary>
public class PeerTestFixture : IAsyncLifetime
{
    private readonly List<PeerServiceFactory> _peerFactories = new();
    private readonly List<HttpClient> _httpClients = new();
    private readonly List<GrpcChannel> _grpcChannels = new();

    public List<PeerInstance> Peers { get; } = new();

    public async ValueTask InitializeAsync()
    {
        // Create 3 peer instances by default
        for (int i = 0; i < 3; i++)
        {
            await AddPeerInstanceAsync($"peer-{i + 1}");
        }
    }

    public async Task<PeerInstance> AddPeerInstanceAsync(string peerId)
    {
        var factory = new PeerServiceFactory
        {
            PeerId = peerId
        };

        _peerFactories.Add(factory);

        var httpClient = factory.CreateClient();
        _httpClients.Add(httpClient);

        var grpcChannel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
        _grpcChannels.Add(grpcChannel);

        var grpcClient = new PeerDiscovery.PeerDiscoveryClient(grpcChannel);

        var instance = new PeerInstance
        {
            PeerId = peerId,
            Factory = factory,
            HttpClient = httpClient,
            GrpcChannel = grpcChannel,
            GrpcClient = grpcClient,
            BaseAddress = httpClient.BaseAddress!.ToString().TrimEnd('/')
        };

        Peers.Add(instance);

        // Give the server a moment to start
        await Task.Delay(100);

        return instance;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _grpcChannels)
        {
            await channel.ShutdownAsync();
            channel.Dispose();
        }

        foreach (var client in _httpClients)
        {
            client.Dispose();
        }

        foreach (var factory in _peerFactories)
        {
            await factory.DisposeAsync();
        }
    }
}

/// <summary>
/// Represents a single peer instance in the test environment
/// </summary>
public class PeerInstance
{
    public required string PeerId { get; init; }
    public required PeerServiceFactory Factory { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required GrpcChannel GrpcChannel { get; init; }
    public required PeerDiscovery.PeerDiscoveryClient GrpcClient { get; init; }
    public required string BaseAddress { get; init; }

    public T GetService<T>() where T : notnull
    {
        return Factory.Services.GetRequiredService<T>();
    }
}
