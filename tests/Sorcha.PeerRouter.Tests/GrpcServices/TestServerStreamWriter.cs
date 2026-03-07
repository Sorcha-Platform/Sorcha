// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

/// <summary>
/// Test implementation of IServerStreamWriter that collects written messages.
/// </summary>
internal sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
{
    private readonly List<T> _messages = [];

    public IReadOnlyList<T> Messages => _messages;

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }
}
