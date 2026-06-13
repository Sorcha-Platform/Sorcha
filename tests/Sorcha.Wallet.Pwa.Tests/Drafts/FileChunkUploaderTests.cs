// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Drafts;

/// <summary>
/// Feature 152 US5 — `FileChunkUploader` chunks + uploads captured media via /api/file-chunks and
/// builds the file-reference object embedded as a payload field value.
/// </summary>
public sealed class FileChunkUploaderTests
{
    [Fact]
    public async Task UploadAsync_PostsChunk_AndBuildsReference()
    {
        var posted = 0;
        var uploader = Create((req, _) =>
        {
            posted++;
            req.RequestUri!.AbsolutePath.Should().Be("/api/file-chunks");
            return Ok("""{"chunkTransactionId":"tx-1","uploadSessionId":"sess-1","saltBase64":"c2FsdA"}""");
        });

        var reference = await uploader.UploadAsync(
            new byte[] { 1, 2, 3 }, "photo.jpg", "image/jpeg", "ws1qcitizen", "reg-1");

        posted.Should().Be(1);
        reference.Should().NotBeNull();
        reference!["masterKeyId"].Should().Be("server-managed");
        reference["uploadSessionId"].Should().Be("sess-1");
        reference["fileName"].Should().Be("photo.jpg");
        ((List<string>)reference["chunkTransactionIds"]!).Should().ContainSingle().Which.Should().Be("tx-1");
        reference["hash"]!.ToString().Should().StartWith("sha256:");
    }

    [Fact]
    public async Task UploadAsync_ChunkFails_ReturnsNull()
    {
        var uploader = Create((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var reference = await uploader.UploadAsync(new byte[] { 1 }, "f", "text/plain", "w", "r");

        reference.Should().BeNull();
    }

    [Fact]
    public async Task AttachAllAsync_InjectsReferenceAtScope()
    {
        var uploader = Create((_, _) => Ok("""{"chunkTransactionId":"tx-1","uploadSessionId":"s","saltBase64":"x"}"""));
        var payload = new Dictionary<string, object?>();
        var media = new List<DraftMedia>
        {
            new("proof.jpg", "image/jpeg", Convert.ToBase64String(new byte[] { 9 }), DateTimeOffset.UtcNow, "/proofOfAddress"),
        };

        var ok = await uploader.AttachAllAsync(media, payload, "ws1q", "reg-1");

        ok.Should().BeTrue();
        payload.Should().ContainKey("/proofOfAddress");
        payload["/proofOfAddress"].Should().BeAssignableTo<Dictionary<string, object?>>();
    }

    [Fact]
    public async Task AttachAllAsync_UploadFails_ReturnsFalse()
    {
        var uploader = Create((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var payload = new Dictionary<string, object?>();
        var media = new List<DraftMedia>
        {
            new("p.jpg", "image/jpeg", Convert.ToBase64String(new byte[] { 1 }), DateTimeOffset.UtcNow, "/x"),
        };

        (await uploader.AttachAllAsync(media, payload, "w", "r")).Should().BeFalse();
    }

    private static FileChunkUploader Create(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://test.example.com") };
        return new FileChunkUploader(http);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request, ct));
    }
}
