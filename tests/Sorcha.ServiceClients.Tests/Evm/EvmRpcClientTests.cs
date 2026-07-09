// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Evm;

namespace Sorcha.ServiceClients.Tests.Evm;

public class EvmRpcClientTests
{
    private const string Registry = "0xdca7ef03e98e0dc2b855be647c39abe984fcf21b";

    [Fact]
    public async Task CallAsync_UnconfiguredChain_ReturnsNotConfigured_NoHttpCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = Client(handler, rpc: null); // no chain configured

        var result = await client.CallAsync(1, Registry, "0xdeadbeef");

        result.Outcome.Should().Be(EvmRpcOutcome.NotConfigured);
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CallAsync_SuccessfulResult_ReturnsOk()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"jsonrpc":"2.0","id":1,"result":"0x0000000000000000000000000000000000000000000000000000000000000001"}""");
        var client = Client(handler, rpc: "https://rpc.example.test/");

        var result = await client.CallAsync(1, Registry, "0xdeadbeef");

        result.Outcome.Should().Be(EvmRpcOutcome.Ok);
        result.Value.Should().Be("0x0000000000000000000000000000000000000000000000000000000000000001");
    }

    [Fact]
    public async Task CallAsync_RpcErrorMember_ReturnsError()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"execution reverted"}}""");
        var client = Client(handler, rpc: "https://rpc.example.test/");

        (await client.CallAsync(1, Registry, "0xdeadbeef")).Outcome.Should().Be(EvmRpcOutcome.Error);
    }

    [Fact]
    public async Task CallAsync_Non200_ReturnsError()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        var client = Client(handler, rpc: "https://rpc.example.test/");

        (await client.CallAsync(1, Registry, "0xdeadbeef")).Outcome.Should().Be(EvmRpcOutcome.Error);
    }

    [Fact]
    public async Task CallAsync_PrivateHost_BlockedBySsrf_ReturnsError()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = Client(handler, rpc: "https://10.0.0.1/", allowPrivate: false); // private IP literal

        (await client.CallAsync(1, Registry, "0xdeadbeef")).Outcome.Should().Be(EvmRpcOutcome.Error);
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CallAsync_NonHttpsEndpoint_ReturnsError()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = Client(handler, rpc: "http://rpc.example.test/"); // not HTTPS

        (await client.CallAsync(1, Registry, "0xdeadbeef")).Outcome.Should().Be(EvmRpcOutcome.Error);
    }

    [Fact]
    public async Task GetLogsAsync_SuccessfulArray_ReturnsOkLogs()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"jsonrpc":"2.0","id":1,"result":[{"topics":["0xabc","0xdef"],"data":"0x1234"}]}""");
        var client = Client(handler, rpc: "https://rpc.example.test/");

        var result = await client.GetLogsAsync(1, Registry, [null, "0xdef"], 100);

        result.Outcome.Should().Be(EvmRpcOutcome.Ok);
        result.Logs.Should().ContainSingle();
        result.Logs![0].Topics.Should().Equal("0xabc", "0xdef");
        result.Logs[0].Data.Should().Be("0x1234");
    }

    private static EvmRpcClient Client(StubHandler handler, string? rpc, bool allowPrivate = true)
    {
        // Unit tests use a stub handler + fake hostnames; skip the real-DNS SSRF check by default
        // (the dedicated SSRF test opts back in with allowPrivate: false + a private IP literal).
        var options = new EvmRpcOptions { AllowPrivateAddresses = allowPrivate };
        if (rpc is not null)
        {
            options.Rpc["1"] = rpc;
        }

        return new EvmRpcClient(
            new HttpClient(handler),
            Options.Create(options),
            NullLogger<EvmRpcClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
