// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

using Grpc.Net.Client;

namespace Sorcha.PeerRouter.Services;

/// <summary>
/// Singleton pool of gRPC channels reused across all relay requests.
/// Ensures channels survive gRPC service instance lifetimes and are properly disposed on shutdown.
/// </summary>
public sealed class RouterChannelPool : IDisposable
{
    private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();

    /// <summary>
    /// Gets or creates a pooled gRPC channel for the given address.
    /// </summary>
    public GrpcChannel GetOrCreateChannel(string address) =>
        _channels.GetOrAdd(address, addr => GrpcChannel.ForAddress(addr));

    /// <summary>
    /// Removes and disposes a single pooled channel so the next call creates a fresh one.
    /// </summary>
    public void EvictChannel(string address)
    {
        if (_channels.TryRemove(address, out var channel))
        {
            channel.Dispose();
        }
    }

    /// <summary>
    /// Disposes all pooled gRPC channels.
    /// </summary>
    public void Dispose()
    {
        foreach (var (_, channel) in _channels)
        {
            channel.Dispose();
        }

        _channels.Clear();
    }
}
