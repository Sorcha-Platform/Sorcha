// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Channels;

using Grpc.Core;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

/// <summary>
/// Test implementation of IAsyncStreamReader that reads from a pre-populated channel.
/// </summary>
internal sealed class TestAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    private readonly ChannelReader<T> _reader;

    public TestAsyncStreamReader(IEnumerable<T> items)
    {
        var channel = Channel.CreateUnbounded<T>();
        foreach (var item in items)
        {
            channel.Writer.TryWrite(item);
        }
        channel.Writer.Complete();
        _reader = channel.Reader;
    }

    public T Current { get; private set; } = default!;

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (await _reader.WaitToReadAsync(cancellationToken))
        {
            if (_reader.TryRead(out var item))
            {
                Current = item;
                return true;
            }
        }
        return false;
    }
}
