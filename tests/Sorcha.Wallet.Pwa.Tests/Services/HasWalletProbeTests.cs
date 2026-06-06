// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Pwa.Services.Wallet;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Feature 149 — covers <see cref="HasWalletProbe"/> HTTP-shape handling:
/// 200 true / 200 false parsing and the fail-safe (transient failure → true)
/// that keeps a real wallet owner on the existing pair flow.
/// </summary>
public sealed class HasWalletProbeTests
{
    [Fact]
    public async Task HasWalletAsync_200_True_ReturnsTrue()
    {
        var handler = new StubHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/v1/wallet/exists");
            return Ok("""{"hasWallet":true}""");
        });

        var result = await Create(handler).HasWalletAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasWalletAsync_200_False_ReturnsFalse()
    {
        var handler = new StubHandler((_, _) => Ok("""{"hasWallet":false}"""));

        var result = await Create(handler).HasWalletAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasWalletAsync_NetworkError_FailsSafeTrue()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));

        var result = await Create(handler).HasWalletAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasWalletAsync_ServerError_FailsSafeTrue()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await Create(handler).HasWalletAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasWalletAsync_MalformedJson_FailsSafeTrue()
    {
        var handler = new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>not json</html>", Encoding.UTF8, "application/json")
            });

        var result = await Create(handler).HasWalletAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasWalletAsync_EmptyBody_FailsSafeTrue()
    {
        var handler = new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json")
            });

        var result = await Create(handler).HasWalletAsync();

        result.Should().BeTrue();
    }

    private static HasWalletProbe Create(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return new HasWalletProbe(http, NullLogger<HasWalletProbe>.Instance);
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
