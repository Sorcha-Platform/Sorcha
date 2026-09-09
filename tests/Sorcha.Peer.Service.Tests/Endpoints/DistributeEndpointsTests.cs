// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sorcha.Peer.Service.Endpoints;

namespace Sorcha.Peer.Service.Tests.Endpoints;

/// <summary>
/// Feature 108 cross-node fan-out — the submission body must be decided from what was read,
/// never from a header a legitimate sender need not set (#1624).
/// </summary>
/// <remarks>
/// The endpoint used to guard on <c>ContentLength is null or 0</c>. A chunked request carries no
/// Content-Length, so it was refused as bodyless — the same defect that, on blueprint publish
/// (#1618), broke the MCP tool and the CLI while the browser UI kept working, because browsers set
/// Content-Length and Refit/HttpClient send chunked.
/// </remarks>
public class DistributeEndpointsTests
{
    private static HttpRequest RequestWith(byte[] body, long? contentLength)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(body);
        ctx.Request.ContentLength = contentLength;
        return ctx.Request;
    }

    [Fact]
    public async Task ReadSubmissionBodyAsync_ChunkedRequest_ReturnsTheBodyItCarries()
    {
        var payload = Encoding.UTF8.GetBytes("""{"transactionId":"tx-1"}""");

        // A chunked request is exactly this: a body, and no Content-Length.
        var read = await DistributeEndpoints.ReadSubmissionBodyAsync(
            RequestWith(payload, contentLength: null), CancellationToken.None);

        read.Should().Equal(payload,
            "a chunked submission carries no Content-Length, and refusing it as bodyless would "
            + "silently lose the cross-node fan-out while blaming the caller's own body");
    }

    [Fact]
    public async Task ReadSubmissionBodyAsync_ContentLengthRequest_ReturnsTheBodyItCarries()
    {
        var payload = Encoding.UTF8.GetBytes("""{"transactionId":"tx-2"}""");

        var read = await DistributeEndpoints.ReadSubmissionBodyAsync(
            RequestWith(payload, contentLength: payload.Length), CancellationToken.None);

        read.Should().Equal(payload,
            "the framing a sender chooses must not change what the endpoint sees");
    }

    [Fact]
    public async Task ReadSubmissionBodyAsync_GenuinelyEmptyBody_ReturnsEmpty()
    {
        // The pair matters: the point is not that the guard was removed, but that emptiness is
        // still detected — from the bytes rather than from the header.
        var read = await DistributeEndpoints.ReadSubmissionBodyAsync(
            RequestWith([], contentLength: null), CancellationToken.None);

        read.Should().BeEmpty(
            "an empty body is still an empty body, and the handler rejects it on this length");
    }
}
