// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

/// <summary>
/// Minimal ServerCallContext implementation for unit testing gRPC services.
/// </summary>
internal sealed class TestServerCallContext : ServerCallContext
{
    private readonly Metadata _requestHeaders = [];
    private readonly CancellationToken _cancellationToken;
    private readonly Metadata _responseTrailers = [];
    private WriteOptions? _writeOptions;

    private TestServerCallContext(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Creates a new test context with an optional cancellation token.
    /// </summary>
    public static TestServerCallContext Create(CancellationToken cancellationToken = default) =>
        new(cancellationToken);

    protected override string MethodCore => "TestMethod";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "127.0.0.1:50051";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => _requestHeaders;
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore => _responseTrailers;
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get => _writeOptions; set => _writeOptions = value; }
    protected override AuthContext AuthContextCore => new(null, []);

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotImplementedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}
